// Controllers/TownProjectsController.cs
using Helrift.Gate.Api.Services.TownProjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class TownProjectsController : ControllerBase
{
    private readonly ILogger<TownProjectsController> _log;
    private readonly ITownProjectStateService _stateService;
    private readonly ITownProjectContributionService _contributionService;
    private readonly ITownProjectRotationService _rotationService;

    public TownProjectsController(
        ILogger<TownProjectsController> log,
        ITownProjectStateService stateService,
        ITownProjectContributionService contributionService,
        ITownProjectRotationService rotationService)
    {
        _log = log;
        _stateService = stateService;
        _contributionService = contributionService;
        _rotationService = rotationService;
    }

    /// <summary>
    /// Get active projects for a town.
    /// </summary>
    [HttpGet("towns/{townId}/projects")]
    [Authorize(Policy = "ServerOnly")]
    public async Task<IActionResult> GetActiveProjects([FromRoute] string townId, CancellationToken ct)
    {
        var projects = await _stateService.GetActiveProjectsAsync(townId, ct);
        return Ok(new { projects });
    }

    /// <summary>
    /// Get active rewards for a town.
    /// </summary>
    [HttpGet("towns/{townId}/rewards")]
    [Authorize(Policy = "ServerOnly")]
    public async Task<IActionResult> GetActiveRewards([FromRoute] string townId, CancellationToken ct)
    {
        var rewards = await _stateService.GetActiveRewardsAsync(townId, ct);
        return Ok(new { rewards });
    }

    /// <summary>
    /// Apply a contribution to a project (called by game servers).
    /// </summary>
    [HttpPost("towns/{townId}/projects/{instanceId}/contribute")]
    [Authorize(Policy = "ServerOnly")]
    public async Task<IActionResult> ApplyContribution(
        [FromRoute] string townId,
        [FromRoute] string instanceId,
        [FromBody] ContributeRequest request,
        CancellationToken ct)
    {
        try
        {
            var instance = await _contributionService.ApplyContributionAsync(
                townId,
                instanceId,
                request.ContributorCharacterId,
                request.ContributorAccountId,
                request.ContributionUnits,
                ct);

            return Ok(instance);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Manually trigger weekly reset for a specific town (admin/testing).
    /// </summary>
    [HttpPost("towns/{townId}/weekly-reset")]
    [Authorize(Policy = "ServerOnly")]
    public async Task<IActionResult> TriggerWeeklyReset([FromRoute] string townId, CancellationToken ct)
    {
        await _rotationService.ExecuteWeeklyResetForTownAsync(townId, ct);
        return Ok(new { message = "Weekly reset executed" });
    }

    /// <summary>
    /// Manually trigger crusade startup for a specific town (admin/testing).
    /// </summary>
    [HttpPost("towns/{townId}/crusade-startup")]
    [Authorize(Policy = "ServerOnly")]
    public async Task<IActionResult> TriggerCrusadeStartup(
        [FromRoute] string townId,
        [FromBody] CrusadeStartupRequest request,
        CancellationToken ct)
    {
        await _rotationService.ExecuteCrusadeStartupAsync(townId, request.EventInstanceId, ct);
        return Ok(new { message = "Crusade startup executed" });
    }

    public sealed class ContributeRequest
    {
        public string ContributorCharacterId { get; set; } = string.Empty;
        public string ContributorAccountId { get; set; } = string.Empty;
        public int ContributionUnits { get; set; }
    }

    public sealed class CrusadeStartupRequest
    {
        public string EventInstanceId { get; set; } = string.Empty;
    }
}