// Services/TownProjects/TownProjectRotationService.cs
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts.TownProjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Helrift.Gate.Api.Services.TownProjects;

public sealed class TownProjectRotationService : ITownProjectRotationService
{
    private readonly ILogger<TownProjectRotationService> _log;
    private readonly ITownProjectStateRepository _stateRepo;
    private readonly ITownProjectStateService _stateService;
    private readonly ITownProjectRewardService _rewardService;
    private readonly ITownProjectConfigService _configService;
    private readonly IReadOnlyList<string> _towns;
    private readonly string _realmId;

    public TownProjectRotationService(
        ILogger<TownProjectRotationService> log,
        IConfiguration configuration,
        ITownProjectStateRepository stateRepo,
        ITownProjectStateService stateService,
        ITownProjectRewardService rewardService,
        ITownProjectConfigService configService)
    {
        _log = log;
        _stateRepo = stateRepo;
        _stateService = stateService;
        _rewardService = rewardService;
        _configService = configService;
        _realmId = configuration["RealmId"] ?? "default";
        _towns = configuration.GetSection("TownProjects:Towns").Get<string[]>()
            ?? ["aresden", "elvine"];
    }

    public async Task ExecuteWeeklyResetAsync(CancellationToken ct = default)
    {
        _log.LogInformation("Starting weekly reset for {Count} configured towns", _towns.Count);

        foreach (var townId in _towns)
        {
            try
            {
                await ExecuteWeeklyResetForTownAsync(townId, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed weekly reset for town {TownId}", townId);
            }
        }

        _log.LogInformation("Completed weekly reset for {Count} towns", _towns.Count);
    }

    public async Task ExecuteWeeklyResetForTownAsync(string townId, CancellationToken ct = default)
    {
        _log.LogInformation("Executing weekly reset for town {TownId}", townId);

        // 1. Evaluate existing weekly projects
        var instances = await _stateRepo.GetActiveInstancesAsync(_realmId, townId, ct);
        var weeklyInstances = instances.Where(i =>
            IsCategory(i.DefinitionId, TownProjectCategory.WeeklyGeneral) &&
            (i.Status == TownProjectStatus.Active || i.Status == TownProjectStatus.CompletedPendingActivation)).ToList();

        foreach (var instance in weeklyInstances)
        {
            if (instance.Status == TownProjectStatus.CompletedPendingActivation)
            {
                // Activate reward
                await _rewardService.ActivateProjectRewardAsync(townId, instance.Id, null, ct);
                _log.LogInformation("Activated reward for completed project {InstanceId}", instance.Id);
            }
            else
            {
                // Mark as failed
                instance.Status = TownProjectStatus.Failed;
                instance.Version++;
                await _stateRepo.SaveInstanceAsync(instance, ct);
                _log.LogInformation("Marked incomplete project {InstanceId} as failed", instance.Id);
            }
        }

        // 2. Expire previous weekly buffs
        var rewards = await _stateRepo.GetActiveRewardsAsync(_realmId, townId, ct);
        var now = DateTime.UtcNow;

        foreach (var reward in rewards.Where(r => r.ExpiresAtUtc <= now))
        {
            reward.IsActive = false;
            await _stateRepo.SaveRewardAsync(reward, ct);
            _log.LogInformation("Expired reward {RewardId} for town {TownId}", reward.Id, townId);
        }

        // 3. Roll new weekly projects
        await _stateService.InitializeWeeklyProjectsAsync(townId, ct);

        _log.LogInformation("Completed weekly reset for town {TownId}", townId);
    }

    public async Task ExecuteCrusadeStartupAsync(string townId, string eventInstanceId, CancellationToken ct = default)
    {
        _log.LogInformation("Executing crusade startup for town {TownId}, event {EventInstanceId}", townId, eventInstanceId);

        // 1. Evaluate existing crusade projects
        var instances = await _stateRepo.GetActiveInstancesAsync(_realmId, townId, ct);
        var crusadeInstances = instances.Where(i =>
            IsCategory(i.DefinitionId, TownProjectCategory.CrusadePreparation) &&
            !string.IsNullOrEmpty(i.EventInstanceId) &&
            (i.Status == TownProjectStatus.Active || i.Status == TownProjectStatus.CompletedPendingActivation)).ToList();

        foreach (var instance in crusadeInstances)
        {
            if (instance.Status == TownProjectStatus.CompletedPendingActivation)
            {
                // Activate event reward
                await _rewardService.ActivateProjectRewardAsync(townId, instance.Id, eventInstanceId, ct);
                _log.LogInformation("Activated crusade reward for completed project {InstanceId}", instance.Id);
            }
            else
            {
                // Mark as failed
                instance.Status = TownProjectStatus.Failed;
                instance.Version++;
                await _stateRepo.SaveInstanceAsync(instance, ct);
                _log.LogInformation("Marked incomplete crusade project {InstanceId} as failed", instance.Id);
            }
        }

        // 2. Roll next crusade projects
        await _stateService.InitializeCrusadeProjectsAsync(townId, eventInstanceId, ct);

        _log.LogInformation("Completed crusade startup for town {TownId}", townId);
    }

    private bool IsCategory(string definitionId, TownProjectCategory category)
    {
        var definition = _configService.GetDefinition(definitionId);
        if (definition == null)
            return false;

        return definition.Category == category;
    }
}