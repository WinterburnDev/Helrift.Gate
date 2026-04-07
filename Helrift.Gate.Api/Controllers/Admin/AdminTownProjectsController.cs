// Controllers/Admin/AdminTownProjectsController.cs
using Helrift.Gate.Api.Services.TownProjects;
using Helrift.Gate.Contracts.TownProjects;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers.Admin;

[ApiController]
[Route("admin/api/towns")]
public sealed class AdminTownProjectsController(
    ITownProjectStateService stateService,
    ITownProjectConfigService configService) : ControllerBase
{
    /// <summary>
    /// Returns active project instances and active rewards for a town, merged with
    /// definition display metadata for dashboard rendering.
    /// </summary>
    [HttpGet("{townId}/projects/state")]
    public async Task<IActionResult> GetTownProjectState([FromRoute] string townId, CancellationToken ct)
    {
        var instances = await stateService.GetActiveProjectsAsync(townId, ct);
        var rewards = await stateService.GetActiveRewardsAsync(townId, ct);
        var definitions = configService.GetAllDefinitions();
        var now = DateTime.UtcNow;

        var projects = instances.Select(i =>
        {
            definitions.TryGetValue(i.DefinitionId, out var def);
            var progressPct = i.TargetProgress > 0
                ? Math.Round((double)i.CurrentProgress / i.TargetProgress * 100, 1)
                : 0.0;
            var remainingSeconds = i.ExpiresAtUtc.HasValue
                ? Math.Max(0, (int)(i.ExpiresAtUtc.Value - now).TotalSeconds)
                : (int?)null;

            return new TownProjectStateEntry
            {
                InstanceId = i.Id,
                DefinitionId = i.DefinitionId,
                Name = def?.Name ?? i.DefinitionId,
                Description = def?.Description ?? string.Empty,
                Category = i.Status == TownProjectStatus.Active || i.Status == TownProjectStatus.CompletedPendingActivation
                    ? (def?.Category ?? TownProjectCategory.Unknown)
                    : TownProjectCategory.Unknown,
                ContributionType = def?.ContributionType ?? TownProjectContributionType.Unknown,
                Status = i.Status,
                CurrentProgress = i.CurrentProgress,
                TargetProgress = i.TargetProgress,
                ProgressPct = progressPct,
                StartedAtUtc = i.StartedAtUtc,
                ExpiresAtUtc = i.ExpiresAtUtc,
                RemainingSeconds = remainingSeconds
            };
        }).ToList();

        var activeRewardEntries = rewards.Select(r => new TownRewardStateEntry
        {
            RewardId = r.Id,
            RewardType = r.RewardType.ToString(),
            RewardValue = r.RewardValue,
            ActivatedAtUtc = r.ActivatedAtUtc,
            ExpiresAtUtc = r.ExpiresAtUtc,
            RemainingSeconds = Math.Max(0, (int)(r.ExpiresAtUtc - now).TotalSeconds)
        }).ToList();

        return Ok(new TownProjectsStateResponse
        {
            TownId = townId,
            Projects = projects,
            ActiveRewards = activeRewardEntries
        });
    }
}

public sealed class TownProjectsStateResponse
{
    public string TownId { get; init; } = string.Empty;
    public List<TownProjectStateEntry> Projects { get; init; } = [];
    public List<TownRewardStateEntry> ActiveRewards { get; init; } = [];
}

public sealed class TownProjectStateEntry
{
    public string InstanceId { get; init; } = string.Empty;
    public string DefinitionId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public TownProjectCategory Category { get; init; }
    public TownProjectContributionType ContributionType { get; init; }
    public TownProjectStatus Status { get; init; }
    public int CurrentProgress { get; init; }
    public int TargetProgress { get; init; }
    public double ProgressPct { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public int? RemainingSeconds { get; init; }
}

public sealed class TownRewardStateEntry
{
    public string RewardId { get; init; } = string.Empty;
    public string RewardType { get; init; } = string.Empty;
    public string RewardValue { get; init; } = string.Empty;
    public DateTime ActivatedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public int RemainingSeconds { get; init; }
}
