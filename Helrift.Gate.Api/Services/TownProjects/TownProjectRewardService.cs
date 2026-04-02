// Services/TownProjects/TownProjectRewardService.cs
using Helrift.Gate.Api.Services.Deliveries;
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;
using Helrift.Gate.Contracts.TownProjects;
using Helrift.Gate.Infrastructure;
using Helrift.Gate.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Helrift.Gate.Api.Services.TownProjects;

public sealed class TownProjectRewardService : ITownProjectRewardService
{
    private readonly ILogger<TownProjectRewardService> _log;
    private readonly ITownProjectConfigService _configService;
    private readonly ITownProjectStateRepository _stateRepo;
    private readonly IGuildDataProvider _guildDataProvider;
    private readonly IDeliveryService _deliveryService;
    private readonly IGameServerConnectionRegistry _gsRegistry;
    private readonly string _realmId;

    public TownProjectRewardService(
        ILogger<TownProjectRewardService> log,
        IConfiguration configuration,
        ITownProjectConfigService configService,
        ITownProjectStateRepository stateRepo,
        IGuildDataProvider guildDataProvider,
        IDeliveryService deliveryService,
        IGameServerConnectionRegistry gsRegistry)
    {
        _log = log;
        _configService = configService;
        _stateRepo = stateRepo;
        _guildDataProvider = guildDataProvider;
        _deliveryService = deliveryService;
        _gsRegistry = gsRegistry;
        _realmId = configuration["RealmId"] ?? "default";
    }

    public async Task ActivateProjectRewardAsync(
        string townId,
        string instanceId,
        string? eventInstanceId = null,
        CancellationToken ct = default)
    {
        var instance = await _stateRepo.GetInstanceAsync(_realmId, townId, instanceId, ct);
        if (instance == null)
        {
            _log.LogWarning("Cannot activate reward: instance {InstanceId} not found", instanceId);
            return;
        }

        if (instance.Status != TownProjectStatus.CompletedPendingActivation)
        {
            _log.LogWarning(
                "Cannot activate reward: instance {InstanceId} has status {Status}",
                instanceId, instance.Status);
            return;
        }

        var definition = _configService.GetDefinition(instance.DefinitionId);
        if (definition == null)
        {
            _log.LogWarning("Cannot activate reward: definition {DefinitionId} not found", instance.DefinitionId);
            return;
        }

        var existingReward = (await _stateRepo.GetActiveRewardsAsync(_realmId, townId, ct))
            .FirstOrDefault(r => r.IsActive && string.Equals(r.ProjectInstanceId, instance.Id, StringComparison.OrdinalIgnoreCase));

        if (existingReward != null)
        {
            _log.LogInformation(
                "Reward activation skipped: reward already active for instance {InstanceId} (RewardId={RewardId})",
                instance.Id,
                existingReward.Id);

            if (instance.Status != TownProjectStatus.CompletedActivated)
            {
                instance.Status = TownProjectStatus.CompletedActivated;
                instance.ActivatedAtUtc = existingReward.ActivatedAtUtc;
                instance.Version++;
                await _stateRepo.SaveInstanceAsync(instance, ct);
            }

            return;
        }

        var now = DateTime.UtcNow;
        var expiresAt = definition.RewardDurationSeconds > 0
            ? now.AddSeconds(definition.RewardDurationSeconds)
            : now.AddYears(100); // Effectively permanent

        // Create reward state (for buffs, tracked by game servers)
        var rewardState = new TownProjectRewardState
        {
            Id = $"reward_{Guid.NewGuid():N}",
            TownId = townId,
            RealmId = _realmId,
            ProjectDefinitionId = instance.DefinitionId,
            ProjectInstanceId = instance.Id,
            RewardType = definition.RewardType,
            RewardScope = definition.RewardScope,
            RewardValue = definition.RewardValue,
            ActivatedAtUtc = now,
            ExpiresAtUtc = expiresAt,
            EventInstanceId = eventInstanceId,
            IsActive = true
        };

        await _stateRepo.SaveRewardAsync(rewardState, ct);

        // Update instance status
        instance.Status = TownProjectStatus.CompletedActivated;
        instance.ActivatedAtUtc = now;
        instance.Version++;
        await _stateRepo.SaveInstanceAsync(instance, ct);

        _log.LogInformation(
            "Activated project reward: Town={TownId}, Instance={InstanceId}, Definition={DefinitionId}, RewardType={RewardType}",
            townId, instanceId, instance.DefinitionId, definition.RewardType);

        // Distribute individual rewards if applicable
        await DistributeIndividualRewardsAsync(townId, instance, definition, ct);

        // Broadcast to game servers
        await BroadcastRewardStateAsync(rewardState, ct);
    }

    private async Task DistributeIndividualRewardsAsync(
        string townId,
        TownProjectInstance instance,
        TownProjectDefinition definition,
        CancellationToken ct)
    {
        // Gate only fan-outs individual notifications for individual-scope rewards.
        if (definition.RewardScope != TownProjectRewardScope.Individual)
            return;

        // V1: Only support AllCitizens mode
        if (definition.IndividualRewardMode != TownProjectIndividualRewardMode.AllCitizens)
        {
            _log.LogWarning(
                "Individual reward mode {Mode} not yet supported for project {DefinitionId}",
                definition.IndividualRewardMode, definition.Id);
            return;
        }

        // Resolve citizens of the town (guild members)
        var guild = await _guildDataProvider.GetAsync(townId, ct);
        if (guild == null || guild.MemberCharacterIds.Count == 0)
        {
            _log.LogInformation("No citizens found for town {TownId}, skipping individual rewards", townId);
            return;
        }

        _log.LogInformation(
            "Distributing individual rewards to {Count} citizens of town {TownId}",
            guild.MemberCharacterIds.Count, townId);

        // Idempotency key to prevent double-sends
        var baseIdempotencyKey = $"townproject_reward_{instance.Id}_{instance.ActivatedAtUtc:yyyyMMddHHmmss}";

        foreach (var citizenCharacterId in guild.MemberCharacterIds)
        {
            try
            {
                var idempotencyKey = $"{baseIdempotencyKey}_{citizenCharacterId}";

                await _deliveryService.CreateSystemDeliveryAsync(new CreateSystemDeliveryRequest
                {
                    RealmId = _realmId,
                    IdempotencyKey = idempotencyKey,
                    SenderId = "system",
                    SenderDisplayName = "Town Council",
                    RecipientAccountId = string.Empty,
                    RecipientCharacterId = citizenCharacterId,
                    RecipientInventory = "inventory",
                    Subject = $"Town Project Activated: {definition.Name}",
                    Body = $"Project '{definition.Name}' is now active. Effect: {definition.RewardType} ({definition.RewardValue}).",
                    ReturnToSenderOnExpiry = false,
                    ExpiresUtc = DateTime.UtcNow.AddDays(30),
                    CreatedByActorType = "townproject",
                    CreatedByActorId = instance.Id,
                    Attachments = new List<ParcelItemAttachmentRequest>()
                }, ct);

                _log.LogInformation(
                    "Distributed individual reward to citizen {CharacterId} for project {InstanceId}",
                    citizenCharacterId, instance.Id);
            }
            catch (Exception ex)
            {
                _log.LogError(
                    ex,
                    "Failed to distribute individual reward to citizen {CharacterId} for project {InstanceId}",
                    citizenCharacterId, instance.Id);
            }
        }
    }

    private async Task BroadcastRewardStateAsync(TownProjectRewardState rewardState, CancellationToken ct)
    {
        try
        {
            var envelope = new
            {
                type = "townprojects.reward.activated",
                payload = rewardState
            };

            await _gsRegistry.BroadcastAsync(envelope, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to broadcast reward state for {RewardId}", rewardState.Id);
        }
    }
}