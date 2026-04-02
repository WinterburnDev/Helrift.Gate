// Services/TownProjects/TownProjectStateService.cs
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts.TownProjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Helrift.Gate.Api.Services.TownProjects;

public sealed class TownProjectStateService : ITownProjectStateService
{
    private readonly ILogger<TownProjectStateService> _log;
    private readonly ITownProjectConfigService _configService;
    private readonly ITownProjectStateRepository _stateRepo;
    private readonly string _realmId;

    public TownProjectStateService(
        ILogger<TownProjectStateService> log,
        IConfiguration configuration,
        ITownProjectConfigService configService,
        ITownProjectStateRepository stateRepo)
    {
        _log = log;
        _configService = configService;
        _stateRepo = stateRepo;
        _realmId = configuration["RealmId"] ?? "default";
    }

    public async Task<IReadOnlyList<TownProjectInstance>> GetActiveProjectsAsync(string townId, CancellationToken ct = default)
    {
        var all = await _stateRepo.GetActiveInstancesAsync(_realmId, townId, ct);
        var now = DateTime.UtcNow;

        return all
            .Where(i => i.Status == TownProjectStatus.Active || i.Status == TownProjectStatus.CompletedPendingActivation)
            .Where(i => !i.ExpiresAtUtc.HasValue || i.ExpiresAtUtc.Value > now)
            .ToList();
    }

    public async Task<TownProjectInstance?> GetProjectInstanceAsync(string townId, string instanceId, CancellationToken ct = default)
    {
        return await _stateRepo.GetInstanceAsync(_realmId, townId, instanceId, ct);
    }

    public async Task<IReadOnlyList<TownProjectRewardState>> GetActiveRewardsAsync(string townId, CancellationToken ct = default)
    {
        var all = await _stateRepo.GetActiveRewardsAsync(_realmId, townId, ct);
        var now = DateTime.UtcNow;

        return all
            .Where(r => r.IsActive)
            .Where(r => r.ExpiresAtUtc > now)
            .ToList();
    }

    public async Task InitializeWeeklyProjectsAsync(string townId, CancellationToken ct = default)
    {
        _log.LogInformation("Initializing weekly projects for town {TownId}", townId);

        var existing = await _stateRepo.GetActiveInstancesAsync(_realmId, townId, ct);
        var existingWeeklyDefinitionIds = new HashSet<string>(
            existing
                .Where(i => i.Status == TownProjectStatus.Active || i.Status == TownProjectStatus.CompletedPendingActivation)
                .Select(i => i.DefinitionId),
            StringComparer.OrdinalIgnoreCase);

        var definitions = _configService.GetAllDefinitions();
        var weeklyDefs = definitions.Values
            .Where(d => d.Category == TownProjectCategory.WeeklyGeneral && d.IsEnabled)
            .ToList();

        if (weeklyDefs.Count == 0)
        {
            _log.LogWarning("No enabled weekly project definitions found");
            return;
        }

        // Roll up to 5 random weekly projects (or fewer if already active).
        var random = new Random();
        var selected = weeklyDefs
            .Where(d => !existingWeeklyDefinitionIds.Contains(d.Id))
            .OrderBy(_ => random.Next())
            .Take(5)
            .ToList();

        if (selected.Count == 0)
        {
            _log.LogInformation("Weekly initialization skipped for town {TownId}: active definitions already present", townId);
            return;
        }

        var now = DateTime.UtcNow;
        var expiresAt = now.AddDays(7); // Weekly projects expire in 7 days

        foreach (var def in selected)
        {
            var instance = new TownProjectInstance
            {
                Id = $"weekly_{Guid.NewGuid():N}",
                DefinitionId = def.Id,
                TownId = townId,
                RealmId = _realmId,
                Status = TownProjectStatus.Active,
                CurrentProgress = 0,
                TargetProgress = def.TargetProgress,
                StartedAtUtc = now,
                ExpiresAtUtc = expiresAt,
                Version = 1
            };

            await _stateRepo.SaveInstanceAsync(instance, ct);
            _log.LogInformation(
                "Created weekly project instance {InstanceId} for town {TownId}: {DefinitionId}",
                instance.Id, townId, def.Id);
        }
    }

    public async Task InitializeCrusadeProjectsAsync(string townId, string eventInstanceId, CancellationToken ct = default)
    {
        _log.LogInformation("Initializing crusade projects for town {TownId}, event {EventInstanceId}", townId, eventInstanceId);

        var existing = await _stateRepo.GetActiveInstancesAsync(_realmId, townId, ct);
        var existingCrusadeDefinitionIds = new HashSet<string>(
            existing
                .Where(i => i.Status == TownProjectStatus.Active || i.Status == TownProjectStatus.CompletedPendingActivation)
                .Where(i => string.Equals(i.EventInstanceId, eventInstanceId, StringComparison.OrdinalIgnoreCase))
                .Select(i => i.DefinitionId),
            StringComparer.OrdinalIgnoreCase);

        var definitions = _configService.GetAllDefinitions();
        var crusadeDefs = definitions.Values
            .Where(d => d.Category == TownProjectCategory.CrusadePreparation && d.IsEnabled)
            .ToList();

        if (crusadeDefs.Count == 0)
        {
            _log.LogWarning("No enabled crusade project definitions found");
            return;
        }

        // Use all crusade projects not already active for this event.
        var now = DateTime.UtcNow;

        foreach (var def in crusadeDefs.Where(d => !existingCrusadeDefinitionIds.Contains(d.Id)))
        {
            var instance = new TownProjectInstance
            {
                Id = $"crusade_{Guid.NewGuid():N}",
                DefinitionId = def.Id,
                TownId = townId,
                RealmId = _realmId,
                Status = TownProjectStatus.Active,
                CurrentProgress = 0,
                TargetProgress = def.TargetProgress,
                StartedAtUtc = now,
                EventInstanceId = eventInstanceId,
                Version = 1
            };

            await _stateRepo.SaveInstanceAsync(instance, ct);
            _log.LogInformation(
                "Created crusade project instance {InstanceId} for town {TownId}: {DefinitionId}",
                instance.Id, townId, def.Id);
        }

        if (existingCrusadeDefinitionIds.Count > 0)
        {
            _log.LogInformation(
                "Crusade initialization for town {TownId}, event {EventInstanceId} skipped {SkippedCount} existing definitions",
                townId,
                eventInstanceId,
                existingCrusadeDefinitionIds.Count);
        }
    }
}