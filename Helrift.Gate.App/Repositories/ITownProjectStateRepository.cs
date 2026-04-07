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

    // Requirement History (anti-repetition support)

    /// <summary>
    /// Returns the ID of the requirement entry that was most recently rolled for the given
    /// (realmId, townId, definitionId) combination, or null if no history exists.
    /// Used by the selection logic to prevent immediate repetition.
    /// Path: /realms/{realmId}/townProjects/requirementHistory/{townId}/{definitionId}/lastEntryId
    /// </summary>
    Task<string?> GetLastRequirementEntryAsync(string realmId, string townId, string definitionId, CancellationToken ct = default);

    /// <summary>
    /// Persists the most recently rolled requirement entry ID for anti-repetition tracking.
    /// </summary>
    Task SaveLastRequirementEntryAsync(string realmId, string townId, string definitionId, string entryId, CancellationToken ct = default);
}