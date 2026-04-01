// Services/TownProjects/ITownProjectContributionService.cs
using Helrift.Gate.Contracts.TownProjects;

namespace Helrift.Gate.Api.Services.TownProjects;

/// <summary>
/// Service for handling contributions to Town Projects.
/// </summary>
public interface ITownProjectContributionService
{
    /// <summary>
    /// Apply a contribution to a project instance.
    /// </summary>
    Task<TownProjectInstance> ApplyContributionAsync(
        string townId,
        string instanceId,
        string contributorCharacterId,
        string contributorAccountId,
        int contributionUnits,
        CancellationToken ct = default);
}