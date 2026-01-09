using Helrift.Gate.Api.Services.Accounts;
using Helrift.Gate.Contracts.Leaderboards;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Helrift.Gate.Services.Leaderboards;

public interface ILeaderboardService
{
    Task IngestAsync(LeaderboardIncrementData data, CancellationToken ct);
    Task<LeaderboardResponseDto> GetTopAsync(GetLeaderboardQuery query, CancellationToken ct);
}

public sealed class LeaderboardService : ILeaderboardService
{
    private readonly ILeaderboardRepository _repo;
    private readonly ICharacterDirectoryService _directory;

    // Phase 1: always maintain these windows so the UI can filter.
    private static readonly LeaderboardWindowType[] EnabledWindows =
    {
        LeaderboardWindowType.Daily,
        LeaderboardWindowType.Weekly,
        LeaderboardWindowType.Monthly
    };

    public LeaderboardService(ILeaderboardRepository repo, ICharacterDirectoryService directory)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
    }

    public async Task IngestAsync(LeaderboardIncrementData data, CancellationToken ct)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        // Phase 1 guardrails
        if (data.SubjectType != LeaderboardSubjectType.Character)
            throw new InvalidOperationException("Only Character subject supported.");

        if (string.IsNullOrWhiteSpace(data.SubjectId))
            throw new InvalidOperationException("SubjectId is required.");

        if (data.Delta <= 0)
            throw new InvalidOperationException("Delta must be positive.");

        if (string.IsNullOrWhiteSpace(data.RealmId))
            throw new InvalidOperationException("RealmId is required.");

        if (string.IsNullOrWhiteSpace(data.IdempotencyKey))
            throw new InvalidOperationException("IdempotencyKey is required.");

        // Phase 1: lock to kills
        if (!string.Equals(data.MetricKey, "pvp.kills", StringComparison.Ordinal))
            throw new InvalidOperationException("Unsupported metric in phase 1.");

        // Idempotent insert (if duplicate, bail out cleanly)
        var inserted = await _repo.TryInsertEventAsync(data.IdempotencyKey, data, ct).ConfigureAwait(false);
        if (!inserted)
            return;

        var updatedUtc = DateTime.UtcNow;

        foreach (var window in EnabledWindows)
        {
            var bucketStart = ComputeBucketStartUtc(data.OccurredUtc, window);

            var key = new LeaderboardCounterKey(
                RealmId: data.RealmId,
                Side: data.Side,
                MetricKey: data.MetricKey,
                Window: window,
                BucketStartUtc: bucketStart,
                SubjectType: data.SubjectType,
                SubjectId: data.SubjectId
            );

            await _repo.UpsertCounterIncrementAsync(key, data.Delta, updatedUtc, ct).ConfigureAwait(false);
        }
    }

    public async Task<LeaderboardResponseDto> GetTopAsync(GetLeaderboardQuery query, CancellationToken ct)
    {
        if (query == null)
            throw new ArgumentNullException(nameof(query));

        var limit = Math.Clamp(query.Limit, 1, 200);

        // If caller didn't provide a bucket, default to current bucket for that window.
        var bucketStart = query.BucketStartUtc ?? ComputeBucketStartUtc(DateTime.UtcNow, query.Window);

        var rows = await _repo.GetTopAsync(
            realmId: query.RealmId,
            side: query.Side,
            metricKey: query.MetricKey,
            window: query.Window,
            bucketStartUtc: bucketStart,
            limit: limit,
            ct: ct).ConfigureAwait(false);

        // Resolve characterId -> name (best-effort)
        var ids = rows.Select(r => r.Key.SubjectId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var names = ids.Length == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await _directory.GetNamesByCharacterIdsAsync(query.RealmId, ids, ct).ConfigureAwait(false);

        var items = new List<LeaderboardEntryDto>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var (key, value) = rows[i];

            if (!names.TryGetValue(key.SubjectId, out var displayName) || string.IsNullOrWhiteSpace(displayName))
                displayName = "Unknown";

            items.Add(new LeaderboardEntryDto(
                Rank: i + 1,
                SubjectId: key.SubjectId,
                DisplayName: displayName,
                Value: value));
        }

        return new LeaderboardResponseDto(
            RealmId: query.RealmId,
            Side: query.Side,
            MetricKey: query.MetricKey,
            Window: query.Window,
            BucketStartUtc: bucketStart,
            Items: items);
    }

    private static DateTime ComputeBucketStartUtc(DateTime occurredUtc, LeaderboardWindowType window)
    {
        // Treat incoming as UTC (GS should send UTC). If Unspecified/Local, normalize.
        var utc = occurredUtc.Kind == DateTimeKind.Utc ? occurredUtc : occurredUtc.ToUniversalTime();

        return window switch
        {
            LeaderboardWindowType.Daily =>
                new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc),

            LeaderboardWindowType.Weekly =>
                StartOfIsoWeekUtc(utc),

            LeaderboardWindowType.Monthly =>
                new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc),

            _ => throw new ArgumentOutOfRangeException(nameof(window), window, null)
        };
    }

    // Monday 00:00:00Z
    private static DateTime StartOfIsoWeekUtc(DateTime utc)
    {
        // In .NET, Sunday=0. We want Monday=0 offset.
        var diff = utc.DayOfWeek switch
        {
            DayOfWeek.Monday => 0,
            DayOfWeek.Tuesday => 1,
            DayOfWeek.Wednesday => 2,
            DayOfWeek.Thursday => 3,
            DayOfWeek.Friday => 4,
            DayOfWeek.Saturday => 5,
            DayOfWeek.Sunday => 6,
            _ => 0
        };

        var monday = utc.Date.AddDays(-diff);
        return new DateTime(monday.Year, monday.Month, monday.Day, 0, 0, 0, DateTimeKind.Utc);
    }
}
