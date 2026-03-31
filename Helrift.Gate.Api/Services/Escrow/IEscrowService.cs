using Helrift.Gate.Contracts;

namespace Helrift.Gate.Api.Services.Escrow;

public interface IEscrowService
{
    Task<EscrowContainer> CreateEscrowContainerAsync(CreateEscrowContainerRequest request, CancellationToken ct = default);
    Task<EscrowContainer?> GetEscrowContainerAsync(string realmId, string containerId, CancellationToken ct = default);
    Task<EscrowSummary?> GetEscrowSummaryAsync(string realmId, string containerId, CancellationToken ct = default);
    Task<EscrowIntegrityReport> GetIntegrityReportAsync(string realmId, string containerId, CancellationToken ct = default);
    IReadOnlyList<EscrowCapability> GetSupportMatrix();

    Task<EscrowContainer> AddAssetsAsync(AddEscrowAssetsRequest request, CancellationToken ct = default);
    Task<EscrowContainer> EscrowAssetsAsync(EscrowActionRequest request, CancellationToken ct = default);
    Task<EscrowContainer> MakeAssetsClaimableAsync(EscrowActionRequest request, CancellationToken ct = default);
    Task<EscrowContainer> ClaimAssetsAsync(EscrowActionRequest request, CancellationToken ct = default);
    Task<EscrowContainer> ReleaseAssetsAsync(EscrowActionRequest request, CancellationToken ct = default);
    Task<EscrowContainer> ReturnAssetsAsync(EscrowActionRequest request, CancellationToken ct = default);
    Task<EscrowContainer> ExpireEscrowAsync(EscrowActionRequest request, CancellationToken ct = default);
    Task<EscrowContainer> CancelEscrowAsync(EscrowActionRequest request, CancellationToken ct = default);
}