// Services/TownProjects/ITownProjectRewardService.cs
namespace Helrift.Gate.Api.Services.TownProjects;

/// <summary>
/// Service for activating and distributing Town Project rewards.
/// </summary>
public interface ITownProjectRewardService
{
    /// <summary>
    /// Activate rewards for a completed project.
    /// </summary>
    Task ActivateProjectRewardAsync(
        string townId,
        string instanceId,
        string? eventInstanceId = null,
        CancellationToken ct = default);
}