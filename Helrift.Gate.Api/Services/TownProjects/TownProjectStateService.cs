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
    private readonly Random _rng = new();

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

        var selected = weeklyDefs
            .Where(d => !existingWeeklyDefinitionIds.Contains(d.Id))
            .OrderBy(_ => _rng.Next())
            .Take(5)
            .ToList();

        if (selected.Count == 0)
        {
            _log.LogInformation("Weekly initialization skipped for town {TownId}: active definitions already present", townId);
            return;
        }

        var config = await _configService.GetVersionAsync(_configService.GetConfigVersion(), ct)
            ?? throw new InvalidOperationException("Town Project config version is unavailable during weekly initialization.");

        var now = DateTime.UtcNow;
        var expiresAt = now.AddDays(7);

        foreach (var def in selected)
        {
            var resolved = await ResolveRequirementAsync(townId, def, config, ct);
            var instance = new TownProjectInstance
            {
                Id = $"weekly_{Guid.NewGuid():N}",
                DefinitionId = def.Id,
                RequirementEntryId = resolved.EntryId,
                ResolvedRequirement = resolved,
                TownId = townId,
                RealmId = _realmId,
                Status = TownProjectStatus.Active,
                CurrentProgress = 0,
                TargetProgress = resolved.TargetQuantity,
                StartedAtUtc = now,
                ExpiresAtUtc = expiresAt,
                Version = 1
            };

            await _stateRepo.SaveInstanceAsync(instance, ct);
            _log.LogInformation(
                "Created weekly project instance {InstanceId} for town {TownId}: {DefinitionId} ({EntryId})",
                instance.Id, townId, def.Id, resolved.EntryId);
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

        var config = await _configService.GetVersionAsync(_configService.GetConfigVersion(), ct)
            ?? throw new InvalidOperationException("Town Project config version is unavailable during crusade initialization.");

        var now = DateTime.UtcNow;

        foreach (var def in crusadeDefs.Where(d => !existingCrusadeDefinitionIds.Contains(d.Id)))
        {
            var resolved = await ResolveRequirementAsync(townId, def, config, ct);
            var instance = new TownProjectInstance
            {
                Id = $"crusade_{Guid.NewGuid():N}",
                DefinitionId = def.Id,
                RequirementEntryId = resolved.EntryId,
                ResolvedRequirement = resolved,
                TownId = townId,
                RealmId = _realmId,
                Status = TownProjectStatus.Active,
                CurrentProgress = 0,
                TargetProgress = resolved.TargetQuantity,
                StartedAtUtc = now,
                EventInstanceId = eventInstanceId,
                Version = 1
            };

            await _stateRepo.SaveInstanceAsync(instance, ct);
            _log.LogInformation(
                "Created crusade project instance {InstanceId} for town {TownId}: {DefinitionId} ({EntryId})",
                instance.Id, townId, def.Id, resolved.EntryId);
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

    private async Task<TownProjectResolvedRequirement> ResolveRequirementAsync(
        string townId,
        TownProjectDefinition definition,
        TownProjectConfigRoot config,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(definition.RequirementPoolId) ||
            !config.RequirementPools.TryGetValue(definition.RequirementPoolId, out var pool) ||
            pool.Entries.Count == 0)
        {
            return BuildLegacyFallbackRequirement(definition);
        }

        var selectedEntry = await SelectRequirementEntryAsync(townId, definition.Id, pool, ct);
        var resolvedItemIds = ResolveAllowedItemIds(selectedEntry, config.ItemGroups);

        return new TownProjectResolvedRequirement
        {
            EntryId = selectedEntry.Id,
            ContributionType = selectedEntry.ContributionType,
            TargetQuantity = selectedEntry.TargetQuantity,
            ProgressPerUnit = selectedEntry.ProgressPerUnit,
            ReputationPerUnit = selectedEntry.ReputationPerUnit,
            AllowedItemGroupId = selectedEntry.AllowedItemGroupId,
            AllowedItemIds = resolvedItemIds,
            QualityRule = selectedEntry.QualityRule,
            ConditionRule = selectedEntry.ConditionRule
        };
    }

    private async Task<TownProjectRequirementEntry> SelectRequirementEntryAsync(
        string townId,
        string definitionId,
        TownProjectRequirementPool pool,
        CancellationToken ct)
    {
        var history = await _stateRepo.GetSelectionHistoryAsync(_realmId, townId, definitionId, ct)
            ?? new TownProjectRequirementSelectionHistory
            {
                RealmId = _realmId,
                TownId = townId,
                DefinitionId = definitionId,
                RecentRequirementEntryIds = new List<string>()
            };

        var recent = history.RecentRequirementEntryIds ?? new List<string>();
        var recentSet = new HashSet<string>(recent, StringComparer.OrdinalIgnoreCase);

        var candidates = pool.Entries;
        if (pool.PreventImmediateRepeat && recentSet.Count > 0)
        {
            var filtered = candidates.Where(e => !recentSet.Contains(e.Id)).ToList();
            if (filtered.Count > 0)
                candidates = filtered;
        }

        var selected = SelectWeighted(candidates);

        var newHistory = new List<string>(recent);
        newHistory.Insert(0, selected.Id);

        var maxHistory = Math.Max(pool.RecentHistorySize, pool.PreventImmediateRepeat ? 1 : 0);
        if (maxHistory == 0)
            maxHistory = 1;

        history.RecentRequirementEntryIds = newHistory
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxHistory)
            .ToList();
        history.UpdatedAtUtc = DateTime.UtcNow;

        await _stateRepo.SaveSelectionHistoryAsync(history, ct);

        return selected;
    }

    private TownProjectRequirementEntry SelectWeighted(IReadOnlyList<TownProjectRequirementEntry> entries)
    {
        if (entries.Count == 1)
            return entries[0];

        var totalWeight = entries.Sum(e => Math.Max(1, e.Weight));
        var roll = _rng.Next(0, totalWeight);
        var current = 0;

        foreach (var entry in entries)
        {
            current += Math.Max(1, entry.Weight);
            if (roll < current)
                return entry;
        }

        return entries[^1];
    }

    private static List<string> ResolveAllowedItemIds(
        TownProjectRequirementEntry entry,
        IReadOnlyDictionary<string, TownProjectItemGroup> itemGroups)
    {
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var itemId in entry.AllowedItemIds ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(itemId))
                resolved.Add(itemId);
        }

        if (!string.IsNullOrWhiteSpace(entry.AllowedItemGroupId) &&
            itemGroups.TryGetValue(entry.AllowedItemGroupId, out var group))
        {
            foreach (var itemId in group.ItemIds)
            {
                if (!string.IsNullOrWhiteSpace(itemId))
                    resolved.Add(itemId);
            }
        }

        return resolved.ToList();
    }

    private static TownProjectResolvedRequirement BuildLegacyFallbackRequirement(TownProjectDefinition definition)
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
                ? new List<string>()
                : new List<string> { definition.RequiredItemId }
        };
    }
}
