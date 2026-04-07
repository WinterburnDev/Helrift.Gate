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
            var (entryId, resolvedEntry) = await RollRequirementEntryAsync(def, townId, ct);
            var targetProgress = resolvedEntry?.TargetQuantity ?? def.TargetProgress;

            var instance = new TownProjectInstance
            {
                Id = $"weekly_{Guid.NewGuid():N}",
                DefinitionId = def.Id,
                TownId = townId,
                RealmId = _realmId,
                Status = TownProjectStatus.Active,
                CurrentProgress = 0,
                TargetProgress = targetProgress,
                StartedAtUtc = now,
                ExpiresAtUtc = expiresAt,
                RequirementEntryId = entryId,
                ResolvedRequirement = resolvedEntry,
                Version = 1
            };

            await _stateRepo.SaveInstanceAsync(instance, ct);

            if (entryId != null)
                await _stateRepo.SaveLastRequirementEntryAsync(_realmId, townId, def.Id, entryId, ct);

            _log.LogInformation(
                "Created weekly project instance {InstanceId} for town {TownId}: {DefinitionId}, EntryId={EntryId}",
                instance.Id, townId, def.Id, (entryId ?? "<legacy>").Replace('\n', '_').Replace('\r', '_'));
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
            var (entryId, resolvedEntry) = await RollRequirementEntryAsync(def, townId, ct);
            var targetProgress = resolvedEntry?.TargetQuantity ?? def.TargetProgress;

            var instance = new TownProjectInstance
            {
                Id = $"crusade_{Guid.NewGuid():N}",
                DefinitionId = def.Id,
                TownId = townId,
                RealmId = _realmId,
                Status = TownProjectStatus.Active,
                CurrentProgress = 0,
                TargetProgress = targetProgress,
                StartedAtUtc = now,
                EventInstanceId = eventInstanceId,
                RequirementEntryId = entryId,
                ResolvedRequirement = resolvedEntry,
                Version = 1
            };

            await _stateRepo.SaveInstanceAsync(instance, ct);

            if (entryId != null)
                await _stateRepo.SaveLastRequirementEntryAsync(_realmId, townId, def.Id, entryId, ct);

            _log.LogInformation(
                "Created crusade project instance {InstanceId} for town {TownId}: {DefinitionId}, EntryId={EntryId}",
                instance.Id, townId, def.Id, (entryId ?? "<legacy>").Replace('\n', '_').Replace('\r', '_'));
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

    /// <summary>
    /// Selects a requirement entry from the definition's pool using the configured selection
    /// mode and anti-repetition rules. Returns (null, null) for legacy pool-less definitions.
    /// </summary>
    private async Task<(string? EntryId, TownProjectRequirementEntry? Entry)> RollRequirementEntryAsync(
        TownProjectDefinition def,
        string townId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(def.RequirementPoolId))
            return (null, null);

        var pool = _configService.GetRequirementPool(def.RequirementPoolId);
        if (pool == null || pool.Entries.Count == 0)
        {
            _log.LogWarning(
                "RequirementPoolId '{PoolId}' for definition '{DefinitionId}' references a missing or empty pool. Falling back to legacy fields.",
                def.RequirementPoolId, def.Id);
            return (null, null);
        }

        var candidates = pool.Entries.Where(e => e != null && e.Weight >= 1).ToList();

        if (candidates.Count == 0)
        {
            _log.LogWarning("Pool '{PoolId}' has no valid (weight >= 1) entries.", pool.Id);
            return (null, null);
        }

        // Apply anti-repetition: exclude the last-used entry if there is a valid alternative
        if (pool.PreventImmediateRepeat && candidates.Count > 1)
        {
            var lastEntryId = await _stateRepo.GetLastRequirementEntryAsync(_realmId, townId, def.Id, ct);

            if (!string.IsNullOrWhiteSpace(lastEntryId))
            {
                var filtered = candidates
                    .Where(e => !string.Equals(e.Id, lastEntryId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (filtered.Count > 0)
                    candidates = filtered;
                // else: only one entry or all excluded – allow repetition rather than failing
            }
        }

        TownProjectRequirementEntry chosen;

        if (pool.SelectionMode == RequirementPoolSelectionMode.Sequential)
        {
            // Sequential: pick the first candidate (candidates already excludes the last-used entry
            // when PreventImmediateRepeat is active, so this naturally advances the rotation).
            chosen = candidates[0];
        }
        else
        {
            // WeightedRandom (default)
            chosen = PickWeightedRandom(candidates);
        }

        _log.LogDebug(
            "Rolled requirement entry '{EntryId}' from pool '{PoolId}' for definition '{DefinitionId}' (town={TownId})",
            chosen.Id, pool.Id, def.Id, townId.Replace('\n', '_').Replace('\r', '_'));

        return (chosen.Id, chosen);
    }

    private static TownProjectRequirementEntry PickWeightedRandom(IReadOnlyList<TownProjectRequirementEntry> entries)
    {
        var totalWeight = entries.Sum(e => e.Weight);
        var roll = Random.Shared.Next(totalWeight);
        var cumulative = 0;

        foreach (var entry in entries)
        {
            cumulative += entry.Weight;
            if (roll < cumulative)
                return entry;
        }

        // Fallback – should never reach here if weights are valid
        return entries[^1];
    }
}
