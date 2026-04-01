// Services/TownProjects/ITownProjectRotationService.cs
namespace Helrift.Gate.Api.Services.TownProjects;

/// <summary>
/// Service for managing Town Project lifecycle (weekly reset, crusade events).
/// </summary>
public interface ITownProjectRotationService
{
    /// <summary>
    /// Execute weekly reset for all towns.
    /// </summary>
    Task ExecuteWeeklyResetAsync(CancellationToken ct = default);

    /// <summary>
    /// Execute weekly reset for a specific town.
    /// </summary>
    Task ExecuteWeeklyResetForTownAsync(string townId, CancellationToken ct = default);

    /// <summary>
    /// Execute crusade startup evaluation for a town.
    /// </summary>
    Task ExecuteCrusadeStartupAsync(string townId, string eventInstanceId, CancellationToken ct = default);
}