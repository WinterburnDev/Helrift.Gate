// Controllers/TownProjectsController.cs
using Helrift.Gate.Api.Services.TownProjects;
using Helrift.Gate.Contracts.TownProjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers;

[ApiController]
[Route("api/v1/town-projects")]
[Route("api/[controller]")]
[Authorize(Policy = "ServerOnly")]
public sealed class TownProjectsController : ControllerBase
{
    private readonly ILogger<TownProjectsController> _log;
    private readonly ITownProjectStateService _stateService;
    private readonly ITownProjectContributionService _contributionService;
    private readonly ITownProjectRotationService _rotationService;
    private readonly ITownProjectConfigService _configService;

    public TownProjectsController(
        ILogger<TownProjectsController> log,
        ITownProjectStateService stateService,
        ITownProjectContributionService contributionService,
        ITownProjectRotationService rotationService,
        ITownProjectConfigService configService)
    {
        _log = log;
        _stateService = stateService;
        _contributionService = contributionService;
        _rotationService = rotationService;
        _configService = configService;
    }

    /// <summary>
    /// Get Town Projects config used by this Gate instance.
    /// Unity/game servers should treat this as authoritative and read-only.
    /// </summary>
    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        var metadata = _configService.GetConfigMetadata();
        var definitions = _configService.GetAllDefinitions().Values.ToList();
        var requirementPools = _configService.GetAllRequirementPools().Values.ToList();
        var itemGroups = _configService.GetAllItemGroups().Values.ToList();

        return Ok(new TownProjectsConfigResponse
        {
            Version = metadata.Version,
            UpdatedAt = metadata.UpdatedAt,
            UpdatedBy = metadata.UpdatedBy,
            PublishedAt = metadata.PublishedAt,
            PublishedBy = metadata.PublishedBy,
            Definitions = definitions,
            RequirementPools = requirementPools,
            ItemGroups = itemGroups
        });
    }

    /// <summary>
    /// Get a single authoritative bootstrap payload for a town.
    /// Includes config metadata, active project instances, and active rewards.
    /// </summary>
    [HttpGet("towns/{townId}/bootstrap")]
    public async Task<IActionResult> GetTownBootstrap([FromRoute] string townId, CancellationToken ct)
    {
        var metadata = _configService.GetConfigMetadata();
        var definitions = _configService.GetAllDefinitions().Values.ToList();
        var requirementPools = _configService.GetAllRequirementPools().Values.ToList();
        var itemGroups = _configService.GetAllItemGroups().Values.ToList();
        var projects = await _stateService.GetActiveProjectsAsync(townId, ct);
        var rewards = await _stateService.GetActiveRewardsAsync(townId, ct);

        _log.LogInformation(
            "Town projects bootstrap requested: Town={TownId}, ConfigVersion={Version}, Projects={ProjectCount}, Rewards={RewardCount}",
            townId,
            metadata.Version,
            projects.Count,
            rewards.Count);

        return Ok(new TownProjectsBootstrapResponse
        {
            Config = new TownProjectsConfigResponse
            {
                Version = metadata.Version,
                UpdatedAt = metadata.UpdatedAt,
                UpdatedBy = metadata.UpdatedBy,
                PublishedAt = metadata.PublishedAt,
                PublishedBy = metadata.PublishedBy,
                Definitions = definitions,
                RequirementPools = requirementPools,
                ItemGroups = itemGroups
            },
            Projects = projects,
            Rewards = rewards
        });
    }

    /// <summary>
    /// Get active projects for a town.
    /// </summary>
    [HttpGet("towns/{townId}/projects")]
    public async Task<IActionResult> GetActiveProjects([FromRoute] string townId, CancellationToken ct)
    {
        var projects = await _stateService.GetActiveProjectsAsync(townId, ct);
        return Ok(new { projects });
    }

    /// <summary>
    /// Get active rewards for a town.
    /// </summary>
    [HttpGet("towns/{townId}/rewards")]
    public async Task<IActionResult> GetActiveRewards([FromRoute] string townId, CancellationToken ct)
    {
        var rewards = await _stateService.GetActiveRewardsAsync(townId, ct);
        return Ok(new { rewards });
    }

    /// <summary>
    /// Apply a contribution to a project (called by game servers).
    /// </summary>
    [HttpPost("towns/{townId}/projects/{instanceId}/contribute")]
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
                request.DeliveredItemId,
                request.DeliveredItemQuality,
                request.DeliveredItemEndurance,
                request.DeliveredItemMaxEndurance,
                ct);

            return Ok(instance);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Apply a grouped contribution batch for a single project instance.
    /// Intended for 1-2s server-side batching from Unity game servers.
    /// </summary>
    [HttpPost("towns/{townId}/projects/{instanceId}/contribute-batch")]
    public async Task<IActionResult> ApplyContributionBatch(
        [FromRoute] string townId,
        [FromRoute] string instanceId,
        [FromBody] ContributeBatchRequest request,
        CancellationToken ct)
    {
        if (request?.Items == null || request.Items.Count == 0)
            return BadRequest(new { error = "Batch contains no contribution items." });

        var validItems = request.Items
            .Where(i => i != null && i.ContributionUnits > 0)
            .ToList();

        if (validItems.Count == 0)
            return BadRequest(new { error = "Batch contains no valid contribution units." });

        TownProjectInstance? latest = null;

        try
        {
                foreach (var item in validItems)
            {
                latest = await _contributionService.ApplyContributionAsync(
                    townId,
                    instanceId,
                    item.ContributorCharacterId,
                    item.ContributorAccountId,
                    item.ContributionUnits,
                    item.DeliveredItemId,
                    item.DeliveredItemQuality,
                    item.DeliveredItemEndurance,
                    item.DeliveredItemMaxEndurance,
                    ct);
            }

            _log.LogInformation(
                "Applied contribution batch: Town={TownId}, Instance={InstanceId}, ItemsIn={InputCount}, GroupsApplied={GroupCount}",
                townId,
                instanceId,
                request.Items.Count,
                validItems.Count);

            return Ok(latest);
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
    public async Task<IActionResult> TriggerWeeklyReset([FromRoute] string townId, CancellationToken ct)
    {
        await _rotationService.ExecuteWeeklyResetForTownAsync(townId, ct);
        return Ok(new { message = "Weekly reset executed" });
    }

    /// <summary>
    /// Manually trigger crusade startup for a specific town (admin/testing).
    /// </summary>
    [HttpPost("towns/{townId}/crusade-startup")]
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
        public string? DeliveredItemId { get; set; }
        public TownProjectItemQuality? DeliveredItemQuality { get; set; }
        public int? DeliveredItemEndurance { get; set; }
        public int? DeliveredItemMaxEndurance { get; set; }
    }

    public sealed class ContributeBatchRequest
    {
        public List<ContributeRequest> Items { get; set; } = new();
    }

    public sealed class CrusadeStartupRequest
    {
        public string EventInstanceId { get; set; } = string.Empty;
    }

    public sealed class TownProjectsConfigResponse
    {
        public string Version { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
        public DateTime? PublishedAt { get; set; }
        public string? PublishedBy { get; set; }
        public List<TownProjectDefinition> Definitions { get; set; } = new();
        public List<TownProjectRequirementPool> RequirementPools { get; set; } = new();
        public List<TownProjectItemGroup> ItemGroups { get; set; } = new();
    }

    public sealed class TownProjectsBootstrapResponse
    {
        public TownProjectsConfigResponse Config { get; set; } = new();
        public IReadOnlyList<TownProjectInstance> Projects { get; set; } = Array.Empty<TownProjectInstance>();
        public IReadOnlyList<TownProjectRewardState> Rewards { get; set; } = Array.Empty<TownProjectRewardState>();
    }
}