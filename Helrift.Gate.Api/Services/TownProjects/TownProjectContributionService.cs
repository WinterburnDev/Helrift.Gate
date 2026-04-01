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
        CancellationToken ct = default)
    {
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

        // Calculate progress and reputation
        var progressContributed = contributionUnits * definition.ProgressPerContributionUnit;
        var reputationEarned = contributionUnits * definition.ReputationPerContributionUnit;

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