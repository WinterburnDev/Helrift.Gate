// Services/TownProjects/ITownProjectStateService.cs
using Helrift.Gate.Contracts.TownProjects;

namespace Helrift.Gate.Api.Services.TownProjects;

/// <summary>
/// Service for managing active Town Project instances and rewards.
/// </summary>
public interface ITownProjectStateService
{
    /// <summary>
    /// Get all active project instances for a town.
    /// </summary>
    Task<IReadOnlyList<TownProjectInstance>> GetActiveProjectsAsync(string townId, CancellationToken ct = default);

    /// <summary>
    /// Get a specific project instance.
    /// </summary>
    Task<TownProjectInstance?> GetProjectInstanceAsync(string townId, string instanceId, CancellationToken ct = default);

    /// <summary>
    /// Get all active rewards for a town.
    /// </summary>
    Task<IReadOnlyList<TownProjectRewardState>> GetActiveRewardsAsync(string townId, CancellationToken ct = default);

    /// <summary>
    /// Initialize weekly projects for a town (called during weekly reset).
    /// </summary>
    Task InitializeWeeklyProjectsAsync(string townId, CancellationToken ct = default);

    /// <summary>
    /// Initialize crusade projects for a town.
    /// </summary>
    Task InitializeCrusadeProjectsAsync(string townId, string eventInstanceId, CancellationToken ct = default);
}