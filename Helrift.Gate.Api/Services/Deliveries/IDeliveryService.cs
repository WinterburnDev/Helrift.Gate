using Helrift.Gate.Contracts;

namespace Helrift.Gate.Api.Services.Deliveries;

public interface IDeliveryService
{
    Task<DeliveryRecord> CreatePlayerMessageAsync(CreatePlayerMessageRequest request, CancellationToken ct = default);
    Task<DeliveryRecord> CreateParcelDeliveryAsync(CreateParcelDeliveryRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<DeliveryRecord>> CreateSystemDeliveryAsync(CreateSystemDeliveryRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<DeliveryRecord>> CreateGuildBroadcastAsync(CreateGuildBroadcastRequest request, CancellationToken ct = default);

    Task<DeliveryInboxSummary> GetInboxSummaryAsync(string realmId, string recipientCharacterId, CancellationToken ct = default);
    Task<DeliveryNotificationSummary> GetNotificationSummaryAsync(string realmId, string recipientCharacterId, CancellationToken ct = default);
    Task<IReadOnlyList<DeliveryRecord>> GetInboxAsync(string realmId, string recipientCharacterId, DeliveryListQuery query, CancellationToken ct = default);
    Task<DeliveryRecord?> GetDeliveryAsync(string realmId, string recipientCharacterId, string deliveryId, CancellationToken ct = default);

    Task<DeliveryRecord> MarkReadAsync(MarkDeliveryReadRequest request, CancellationToken ct = default);
    Task<DeliveryRecord> ClaimAssetsAsync(ClaimDeliveryAssetsRequest request, CancellationToken ct = default);

    Task<DeliveryAdminSearchResult> SearchAdminAsync(DeliveryAdminSearchQuery query, CancellationToken ct = default);
    Task<DeliveryAdminDetail?> GetAdminDetailAsync(string realmId, string deliveryId, CancellationToken ct = default);

    Task<bool> DeleteAdminAsync(
        string realmId,
        string deliveryId,
        bool cleanupEscrow = true,
        bool force = false,
        CancellationToken ct = default);
}