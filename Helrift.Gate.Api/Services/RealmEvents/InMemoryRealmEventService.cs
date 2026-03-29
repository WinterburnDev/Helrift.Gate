using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Helrift.Gate.Contracts;
using Microsoft.Extensions.Logging;

namespace Helrift.Gate.Api.Services.RealmEvents;

internal sealed class InMemoryRealmEventService : IRealmEventService, IDisposable
{
    private readonly ILogger<InMemoryRealmEventService> _logger;
    private readonly IHttpClientFactory _httpFactory;

    // persistence
    private readonly ConcurrentDictionary<string, long> _lastSequence = new();
    private readonly ConcurrentDictionary<string, RealmEventEnvelope> _latestSnapshot = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<RealmEventEnvelope>> _recentByRealm = new();

    // dedupe: "instanceId:sequence"
    private readonly ConcurrentDictionary<string, byte> _seen = new();

    // subscribers
    private readonly ConcurrentDictionary<string, SubscriptionInfo> _subs = new(StringComparer.Ordinal);

    // fanout queue
    private readonly Channel<(string subId, RealmEventEnvelope env, int attempt)> _deliverChannel = Channel.CreateUnbounded<(string, RealmEventEnvelope, int)>();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _deliverWorker;

    // config
    private const int MaxRecentPerRealm = 200;
    private const int MaxDeliveryAttempts = 6;

    public InMemoryRealmEventService(ILogger<InMemoryRealmEventService> logger, IHttpClientFactory httpFactory)
    {
        _logger = logger;
        _httpFactory = httpFactory;
        _deliverWorker = Task.Run(DeliveryLoopAsync);
    }

    public async Task<PublishResult> PublishAsync(RealmEventEnvelope env, ClaimsPrincipal publisher, CancellationToken ct)
    {
        if (env == null) throw new ArgumentNullException(nameof(env));
        if (string.IsNullOrWhiteSpace(env.EventInstanceId) || string.IsNullOrWhiteSpace(env.RealmId))
            return new PublishResult(false, env.EventInstanceId, env.Sequence, "Missing fields.");

        // idempotency key
        var dedupeKey = $"{env.EventInstanceId}:{env.Sequence}";

        // check seen
        if (_seen.ContainsKey(dedupeKey))
            return new PublishResult(true, env.EventInstanceId, env.Sequence, "Already processed (idempotent).");

        var last = _lastSequence.GetOrAdd(env.EventInstanceId, 0L);

        if (env.Sequence < last)
        {
            // lower than last sequence and not seen -> indicate conflict
            _logger.LogWarning("[RealmEvent] Out-of-order publish rejected Instance={Instance} Seq={Seq} Last={Last}", env.EventInstanceId, env.Sequence, last);
            return new PublishResult(false, env.EventInstanceId, env.Sequence, $"Sequence {env.Sequence} < last persisted {last}.");
        }

        // accept and persist
        _seen[dedupeKey] = 0;

        // update lastSequence if seq >= last
        _lastSequence.AddOrUpdate(env.EventInstanceId, env.Sequence, (_, old) => Math.Max(old, env.Sequence));

        // if snapshot type, update snapshot store
        if (env.Type?.EndsWith(".snapshot", StringComparison.OrdinalIgnoreCase) == true)
        {
            var snapshotKey = $"{env.RealmId}:{env.EventInstanceId}";
            _latestSnapshot[snapshotKey] = env; // atomic replacement
        }

        // add to recent per realm
        var queue = _recentByRealm.GetOrAdd(env.RealmId, _ => new ConcurrentQueue<RealmEventEnvelope>());
        queue.Enqueue(env);
        while (queue.Count > MaxRecentPerRealm && queue.TryDequeue(out _)) { }

        // enqueue fanout for matching subscribers
        var subs = _subs.Values.Where(s => s.RealmId == env.RealmId && (string.IsNullOrEmpty(s.EventType) || s.EventType == env.EventType)).ToArray();
        foreach (var s in subs)
        {
            await _deliverChannel.Writer.WriteAsync((s.Id, env, 0), ct);
        }

        return new PublishResult(true, env.EventInstanceId, env.Sequence, "Accepted");
    }

    public Task<SubscriptionInfo> SubscribeAsync(SubscriptionRequest req, ClaimsPrincipal registrar, CancellationToken ct)
    {
        if (req == null) throw new ArgumentNullException(nameof(req));
        // Basic auth: require role "server" or a claim "admin"
        var isAdmin = registrar?.IsInRole("admin") == true || registrar?.HasClaim("admin", "true") == true || registrar?.IsInRole("server") == true;
        if (!isAdmin)
            throw new UnauthorizedAccessException("Not authorized to register subscriber.");

        var id = Guid.NewGuid().ToString("D");
        var secret = string.IsNullOrEmpty(req.CallbackAuthToken) ? Guid.NewGuid().ToString("N") : req.CallbackAuthToken;
        var info = new SubscriptionInfo(id, req.RealmId, req.EventType, req.CallbackUrl, secret);
        _subs[id] = info;

        _logger.LogInformation("[RealmEvent] Subscriber registered {Id} realm={Realm} eventType={Type} url={Url}", id, req.RealmId, req.EventType, req.CallbackUrl);
        return Task.FromResult(info);
    }

    public Task<bool> UnsubscribeAsync(string subscriptionId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(subscriptionId)) return Task.FromResult(false);
        return Task.FromResult(_subs.TryRemove(subscriptionId, out _));
    }

    public Task<RealmEventEnvelope?> GetSnapshotAsync(string realmId, string eventInstanceId, CancellationToken ct)
    {
        var key = $"{realmId}:{eventInstanceId}";
        _latestSnapshot.TryGetValue(key, out var env);
        return Task.FromResult<RealmEventEnvelope?>(env);
    }

    public Task<IEnumerable<RealmEventEnvelope>> GetRecentAsync(string realmId, int limit, CancellationToken ct)
    {
        if (!_recentByRealm.TryGetValue(realmId, out var q)) return Task.FromResult(Enumerable.Empty<RealmEventEnvelope>());
        var arr = q.Reverse().Take(limit).ToArray();
        return Task.FromResult<IEnumerable<RealmEventEnvelope>>(arr);
    }

    private async Task DeliveryLoopAsync()
    {
        var reader = _deliverChannel.Reader;
        try
        {
            while (await reader.WaitToReadAsync(_cts.Token))
            {
                while (reader.TryRead(out var item))
                {
                    _ = Task.Run(() => DeliverWithRetryAsync(item.subId, item.env, item.attempt, _cts.Token));
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task DeliverWithRetryAsync(string subscriptionId, RealmEventEnvelope env, int attempt, CancellationToken ct)
    {
        if (!_subs.TryGetValue(subscriptionId, out var sub))
        {
            _logger.LogWarning("[RealmEvent] Delivery target missing {SubId}", subscriptionId);
            return;
        }

        var client = _httpFactory.CreateClient();
        var json = JsonSerializer.Serialize(env);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // include subscription secret as header so receiver can validate
        var req = new HttpRequestMessage(HttpMethod.Post, sub.CallbackUrl)
        {
            Content = content
        };
        req.Headers.Add("X-Gate-Subscription-Token", sub.Secret);

        try
        {
            var resp = await client.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                _logger.LogInformation("[RealmEvent] Delivered Instance={Instance} Seq={Seq} -> {SubId}", env.EventInstanceId, env.Sequence, subscriptionId);
                return;
            }
            _logger.LogWarning("[RealmEvent] Delivery failed status {Status} for Sub={SubId} attempt={Attempt}", (int)resp.StatusCode, subscriptionId, attempt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RealmEvent] Delivery exception Sub={SubId} attempt={Attempt}", subscriptionId, attempt);
        }

        // retry
        attempt++;
        if (attempt >= MaxDeliveryAttempts)
        {
            _logger.LogError("[RealmEvent] Delivery dead-letter Sub={SubId} Instance={Instance} Seq={Seq}", subscriptionId, env.EventInstanceId, env.Sequence);
            return;
        }

        var backoffMs = (int)(1000 * Math.Pow(2, Math.Min(attempt, 6))); // 1s,2s,4s...
        try
        {
            await Task.Delay(backoffMs, ct);
            await _deliverChannel.Writer.WriteAsync((subscriptionId, env, attempt), ct);
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _deliverWorker.Wait(TimeSpan.FromSeconds(5)); } catch { }
    }
}