using Helrift.Gate.Contracts;

namespace Helrift.Gate.App.Repositories;

public sealed record EscrowContainerSnapshot(EscrowContainer Container, string? ConcurrencyToken);

public interface IEscrowRepository
{
    Task<EscrowContainer?> GetContainerAsync(string realmId, string containerId, CancellationToken ct);
    Task<EscrowContainerSnapshot?> GetContainerSnapshotAsync(string realmId, string containerId, CancellationToken ct);
    Task CreateContainerAsync(EscrowContainer container, CancellationToken ct);
    Task<bool> TryReplaceContainerAsync(string realmId, EscrowContainer container, string? concurrencyToken, CancellationToken ct);
    Task<bool> DeleteContainerAsync(string realmId, string containerId, CancellationToken ct);
}