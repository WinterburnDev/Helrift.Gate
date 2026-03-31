using Helrift.Gate.Contracts;

namespace Helrift.Gate.App.Repositories;

public sealed record DeliveryRecordSnapshot(DeliveryRecord Record, string? ConcurrencyToken);

public interface IDeliveryRepository
{
    Task<DeliveryRecord?> GetAsync(string realmId, string deliveryId, CancellationToken ct);
    Task<DeliveryRecordSnapshot?> GetSnapshotAsync(string realmId, string deliveryId, CancellationToken ct);

    Task<bool> TryCreateAsync(DeliveryRecord record, CancellationToken ct);
    Task<bool> TryReplaceAsync(string realmId, DeliveryRecord record, string? concurrencyToken, CancellationToken ct);

    Task<IReadOnlyList<DeliveryRecipientIndexEntry>> ListRecipientEntriesAsync(string realmId, string recipientCharacterId, CancellationToken ct);
    Task<IReadOnlyList<DeliveryRecord>> ListAllAsync(string realmId, CancellationToken ct);

    Task<bool> DeleteAsync(string realmId, string deliveryId, CancellationToken ct);
}