// Services/TownProjects/TownProjectContributionService.cs
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts.TownProjects;
using Helrift.Gate.Infrastructure;
using Helrift.Gate.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Helrift.Gate.Api.Services.TownProjects;

public sealed class TownProjectContributionService : ITownProjectContributionService
{
    private readonly ILogger<TownProjectContributionService> _log;
    private readonly ITownProjectConfigService _configService;
    private readonly ITownProjectStateRepository _stateRepo;
    private readonly IGameServerConnectionRegistry _gsRegistry;
    private readonly string _realmId;

    public TownProjectContributionService(
        ILogger<TownProjectContributionService> log,
        IConfiguration configuration,
        ITownProjectConfigService configService,
        ITownProjectStateRepository stateRepo,
        IGameServerConnectionRegistry gsRegistry)
    {
        _log = log;
        _configService = configService;
        _stateRepo = stateRepo;
        _gsRegistry = gsRegistry;
        _realmId = configuration["RealmId"] ?? "default";
    }

    public async Task<TownProjectInstance> ApplyContributionAsync(
        string townId,
        string instanceId,
        string contributorCharacterId,
        string contributorAccountId,
        int contributionUnits,
        string? deliveredItemId = null,
        TownProjectItemQuality? deliveredItemQuality = null,
        int? deliveredItemEndurance = null,
        int? deliveredItemMaxEndurance = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(contributorCharacterId))
            throw new ArgumentException("Contributor character ID is required", nameof(contributorCharacterId));

        if (string.IsNullOrWhiteSpace(contributorAccountId))
            throw new ArgumentException("Contributor account ID is required", nameof(contributorAccountId));

        if (contributionUnits <= 0)
            throw new ArgumentException("Contribution units must be positive", nameof(contributionUnits));

        var instance = await _stateRepo.GetInstanceAsync(_realmId, townId, instanceId, ct);
        if (instance == null)
            throw new InvalidOperationException($"Project instance {instanceId} not found");

        if (instance.Status != TownProjectStatus.Active)
            throw new InvalidOperationException($"Project {instanceId} is not active (status: {instance.Status})");

        var definition = _configService.GetDefinition(instance.DefinitionId);
        if (definition == null)
            throw new InvalidOperationException($"Project definition {instance.DefinitionId} not found");

        var requirement = instance.ResolvedRequirement ?? BuildLegacyRequirement(definition);

        ValidateContributionInput(
            requirement,
            deliveredItemId,
            deliveredItemQuality,
            deliveredItemEndurance,
            deliveredItemMaxEndurance,
            instanceId);

        // Calculate progress and reputation
        var progressContributed = contributionUnits * requirement.ProgressPerUnit;
        var reputationEarned = contributionUnits * requirement.ReputationPerUnit;

        // Cap progress at target
        var remainingProgress = instance.TargetProgress - instance.CurrentProgress;
        if (progressContributed > remainingProgress)
        {
            progressContributed = remainingProgress;
        }

        // Update instance
        instance.CurrentProgress += progressContributed;
        instance.Version++;

        // Record contribution
        var contribution = new TownProjectContribution
        {
            ContributorCharacterId = contributorCharacterId,
            ContributorAccountId = contributorAccountId,
            ProgressContributed = progressContributed,
            ReputationEarned = reputationEarned,
            ContributedAtUtc = DateTime.UtcNow
        };
        instance.Contributions.Add(contribution);

        // Update lifetime reputation
        if (!instance.ContributorReputation.ContainsKey(contributorCharacterId))
            instance.ContributorReputation[contributorCharacterId] = 0;
        instance.ContributorReputation[contributorCharacterId] += reputationEarned;

        // Check completion
        if (instance.CurrentProgress >= instance.TargetProgress)
        {
            instance.Status = TownProjectStatus.CompletedPendingActivation;
            instance.CompletedAtUtc = DateTime.UtcNow;

            _log.LogInformation(
                "Town project completed: Town={TownId}, Instance={InstanceId}, Definition={DefinitionId}",
                townId, instanceId, instance.DefinitionId);
        }

        // Persist
        await _stateRepo.SaveInstanceAsync(instance, ct);

        _log.LogInformation(
            "Applied contribution: Town={TownId}, Instance={InstanceId}, Contributor={ContributorId}, Progress={Progress}/{Target}, Reputation={Reputation}",
            townId, instanceId, contributorCharacterId, instance.CurrentProgress, instance.TargetProgress, reputationEarned);

        // Fan-out to game servers
        await BroadcastProjectUpdateAsync(instance, ct);

        return instance;
    }

    private static TownProjectResolvedRequirement BuildLegacyRequirement(TownProjectDefinition definition)
    {
        return new TownProjectResolvedRequirement
        {
            EntryId = string.IsNullOrWhiteSpace(definition.RequirementPoolId)
                ? $"legacy_{definition.Id}"
                : definition.RequirementPoolId,
            ContributionType = definition.ContributionType,
            TargetQuantity = definition.TargetProgress,
            ProgressPerUnit = definition.ProgressPerContributionUnit,
            ReputationPerUnit = definition.ReputationPerContributionUnit,
            AllowedItemIds = string.IsNullOrWhiteSpace(definition.RequiredItemId)
                ? []
                : [definition.RequiredItemId]
        };
    }

    private static void ValidateContributionInput(
        TownProjectResolvedRequirement requirement,
        string? deliveredItemId,
        TownProjectItemQuality? deliveredItemQuality,
        int? deliveredItemEndurance,
        int? deliveredItemMaxEndurance,
        string instanceId)
    {
        if (requirement.ContributionType != TownProjectContributionType.ItemDelivery)
            return;

        if (string.IsNullOrWhiteSpace(deliveredItemId))
            throw new InvalidOperationException($"Project {instanceId} expects item delivery with item metadata.");

        if (requirement.AllowedItemIds.Count > 0 &&
            !requirement.AllowedItemIds.Contains(deliveredItemId, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Item '{deliveredItemId}' does not match the active requirement.");
        }

        ValidateQuality(requirement.QualityRule, deliveredItemQuality, deliveredItemId);
        ValidateCondition(requirement.ConditionRule, deliveredItemEndurance, deliveredItemMaxEndurance, deliveredItemId);
    }

    private static void ValidateQuality(ItemQualityRule? rule, TownProjectItemQuality? deliveredQuality, string deliveredItemId)
    {
        if (rule == null || rule.Mode == ItemQualityRuleMode.None)
            return;

        var effective = deliveredQuality ?? TownProjectItemQuality.Unknown;
        if (effective == TownProjectItemQuality.Unknown)
            throw new InvalidOperationException($"Item '{deliveredItemId}' is missing quality metadata required by the active project.");

        if (rule.Mode == ItemQualityRuleMode.Exact && effective != rule.Quality)
            throw new InvalidOperationException($"Item '{deliveredItemId}' must be exactly quality '{rule.Quality}'.");

        if (rule.Mode == ItemQualityRuleMode.Minimum && effective < rule.Quality)
            throw new InvalidOperationException($"Item '{deliveredItemId}' must be quality '{rule.Quality}' or higher.");
    }

    private static void ValidateCondition(ItemConditionRule? rule, int? endurance, int? maxEndurance, string deliveredItemId)
    {
        if (rule == null || rule.Mode == ItemConditionRuleMode.Any)
            return;

        var current = endurance ?? -1;
        var max = maxEndurance ?? -1;

        switch (rule.Mode)
        {
            case ItemConditionRuleMode.PristineOnly:
                if (current < 0 || max <= 0 || current < max)
                    throw new InvalidOperationException($"Item '{deliveredItemId}' must be pristine.");
                return;
            case ItemConditionRuleMode.ExactEndurance:
                if (current != rule.ExactEndurance)
                    throw new InvalidOperationException($"Item '{deliveredItemId}' must have exact endurance {rule.ExactEndurance}.");
                return;
            case ItemConditionRuleMode.MinimumEndurance:
                if (current < rule.MinimumEndurance)
                    throw new InvalidOperationException($"Item '{deliveredItemId}' must have endurance >= {rule.MinimumEndurance}.");
                return;
            case ItemConditionRuleMode.MinimumEndurancePercent:
                if (current < 0 || max <= 0)
                    throw new InvalidOperationException($"Item '{deliveredItemId}' is missing endurance metadata required by the active project.");

                var pct = (int)Math.Floor((double)current * 100d / max);
                if (pct < rule.MinimumEndurancePercent)
                    throw new InvalidOperationException($"Item '{deliveredItemId}' must have endurance >= {rule.MinimumEndurancePercent}%.");
                return;
        }
    }

    private async Task BroadcastProjectUpdateAsync(TownProjectInstance instance, CancellationToken ct)
    {
        try
        {
            var envelope = new
            {
                type = "townprojects.instance.updated",
                payload = instance
            };

            await _gsRegistry.BroadcastAsync(envelope, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to broadcast project update for instance {InstanceId}", instance.Id);
        }
    }
}