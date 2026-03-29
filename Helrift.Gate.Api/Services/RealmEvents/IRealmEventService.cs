using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Helrift.Gate.Contracts;

namespace Helrift.Gate.Api.Services.RealmEvents;

public record PublishResult(bool Accepted, string InstanceId, long Sequence, string? Message = null);

public record SubscriptionRequest(string RealmId, string EventType, string CallbackUrl, string CallbackAuthToken);

public record SubscriptionInfo(string Id, string RealmId, string EventType, string CallbackUrl, string Secret);

public interface IRealmEventService
{
    Task<PublishResult> PublishAsync(RealmEventEnvelope env, ClaimsPrincipal publisher, CancellationToken ct);
    Task<SubscriptionInfo> SubscribeAsync(SubscriptionRequest req, ClaimsPrincipal registrar, CancellationToken ct);
    Task<bool> UnsubscribeAsync(string subscriptionId, CancellationToken ct);
    Task<RealmEventEnvelope?> GetSnapshotAsync(string realmId, string eventInstanceId, CancellationToken ct);
    Task<IEnumerable<RealmEventEnvelope>> GetRecentAsync(string realmId, int limit, CancellationToken ct);
}