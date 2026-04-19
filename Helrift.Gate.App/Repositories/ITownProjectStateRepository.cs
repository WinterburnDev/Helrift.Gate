// App/Repositories/ITownProjectStateRepository.cs
using Helrift.Gate.Contracts.TownProjects;

namespace Helrift.Gate.App.Repositories;

/// <summary>
/// Repository for persisting Town Project runtime state.
/// </summary>
public interface ITownProjectStateRepository
{
    // Project Instances
    Task<TownProjectInstance?> GetInstanceAsync(string realmId, string townId, string instanceId, CancellationToken ct = default);
    Task<IReadOnlyList<TownProjectInstance>> GetActiveInstancesAsync(string realmId, string townId, CancellationToken ct = default);
    Task<bool> SaveInstanceAsync(TownProjectInstance instance, CancellationToken ct = default);
    Task<bool> DeleteInstanceAsync(string realmId, string townId, string instanceId, CancellationToken ct = default);

    // Reward State
    Task<TownProjectRewardState?> GetRewardAsync(string realmId, string townId, string rewardId, CancellationToken ct = default);
    Task<IReadOnlyList<TownProjectRewardState>> GetActiveRewardsAsync(string realmId, string townId, CancellationToken ct = default);
    Task<bool> SaveRewardAsync(TownProjectRewardState reward, CancellationToken ct = default);
    Task<bool> DeleteRewardAsync(string realmId, string townId, string rewardId, CancellationToken ct = default);

    // Requirement selection history
    Task<TownProjectRequirementSelectionHistory?> GetSelectionHistoryAsync(string realmId, string townId, string definitionId, CancellationToken ct = default);
    Task<bool> SaveSelectionHistoryAsync(TownProjectRequirementSelectionHistory history, CancellationToken ct = default);

    // Rotation metadata
    Task<DateTime?> GetLastWeeklyResetSlotUtcAsync(string realmId, CancellationToken ct = default);
    Task<bool> SaveLastWeeklyResetSlotUtcAsync(string realmId, DateTime slotUtc, CancellationToken ct = default);
    Task<bool> TryAcquireWeeklyResetLeaseAsync(string realmId, string ownerId, DateTime nowUtc, TimeSpan leaseDuration, CancellationToken ct = default);
    Task<bool> ReleaseWeeklyResetLeaseAsync(string realmId, string ownerId, CancellationToken ct = default);
}