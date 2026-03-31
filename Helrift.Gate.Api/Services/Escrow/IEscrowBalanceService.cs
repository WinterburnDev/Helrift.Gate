using Helrift.Gate.Contracts;

namespace Helrift.Gate.Api.Services.Escrow;

public sealed record EscrowBalanceMutationRequest(
    string AccountId,
    string CharacterId,
    string BalanceKey,
    long Amount,
    string MutationKey);

public interface IEscrowBalanceService
{
    IReadOnlyList<EscrowCapability> GetCapabilities();
    bool IsSupported(string balanceKey);
    Task EnsureDebitAsync(EscrowBalanceMutationRequest request, CancellationToken ct);
    Task EnsureCreditAsync(EscrowBalanceMutationRequest request, CancellationToken ct);
}