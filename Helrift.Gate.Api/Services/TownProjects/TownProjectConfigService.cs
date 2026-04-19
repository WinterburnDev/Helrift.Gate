// Services/TownProjects/TownProjectConfigService.cs
using System.Text.Json;
using Helrift.Gate.Api.Services.ConfigPlatform;
using Helrift.Gate.Api.Services.TownProjects;
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts.TownProjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Helrift.Gate.Api.Services.TownProjects;

public sealed class TownProjectConfigService :
    ConfigDomainServiceBase<TownProjectConfigRoot, TownProjectDefinition, RealmProjectConfigRef>,
    ITownProjectConfigService
{
    private readonly ILogger<TownProjectConfigService> _log;
    private readonly string _realmId;
    private readonly TownProjectConfigRoot _config;
    private readonly TownProjectConfigMetadata _metadata;

    public TownProjectConfigService(
        ILogger<TownProjectConfigService> log,
        IConfiguration configuration,
        ITownProjectConfigRepository repository)
        : base(repository)
    {
        _log = log;
        _realmId = configuration["RealmId"] ?? "default";

        _log.LogInformation("Loading Town Project config for realm {RealmId}...", _realmId);

        try
        {
            // Load realm config ref
            var realmConfigRef = repository.GetRealmConfigRefAsync(_realmId, CancellationToken.None)
                .GetAwaiter().GetResult();

            if (realmConfigRef == null || string.IsNullOrWhiteSpace(realmConfigRef.ProjectsVersion))
            {
                _log.LogWarning(
                    "Town Project config ref not found for realm {RealmId}. Bootstrapping defaults.",
                    _realmId);

                var bootstrapped = BootstrapDefaults(repository, _realmId);
                realmConfigRef = new RealmProjectConfigRef { ProjectsVersion = bootstrapped.Version };
            }

            var version = realmConfigRef.ProjectsVersion;
            _log.LogInformation("Realm {RealmId} uses Town Project config version: {Version}", _realmId, version);

            // Load versioned config
            var config = repository.GetConfigVersionAsync(version, CancellationToken.None)
                .GetAwaiter().GetResult();

            if (config == null || config.Definitions?.Count == 0 || config.RequirementPools?.Count == 0 || config.ItemGroups?.Count == 0)
            {
                _log.LogWarning(
                    "Town Project config version {Version} was selected for realm {RealmId} but not found or corrupted. Bootstrapping defaults.",
                    version,
                    _realmId);

                var bootstrapped = BootstrapDefaults(repository, _realmId);
                config = bootstrapped;
            }

            _config = config;

            // Validate config
            var validation = Validate(_config);
            if (!validation.IsValid)
            {
                var message = $"Town Project config validation failed with {validation.Issues.Count} issue(s):\n" +
                              string.Join("\n", validation.Issues.Select(i => $"  - [{i.Severity}] {i.Code}: {i.Message}"));
                throw new InvalidOperationException(message);
            }

            foreach (var warning in validation.Issues.Where(i => i.Severity == ConfigValidationSeverity.Warning))
            {
                _log.LogWarning("Town Project config warning: {Code} {Message}", warning.Code, warning.Message);
            }

            // Build metadata
            _metadata = new TownProjectConfigMetadata
            {
                RealmId = _realmId,
                Version = _config.Version,
                UpdatedAt = _config.UpdatedAt,
                UpdatedBy = _config.UpdatedBy,
                PublishedAt = _config.PublishedAt,
                PublishedBy = _config.PublishedBy,
                DefinitionCount = _config.Definitions.Count,
                LoadedAt = DateTime.UtcNow
            };

            _log.LogInformation(
                "Town Project config loaded successfully: Version={Version}, Definitions={Count}, UpdatedBy={UpdatedBy}, UpdatedAt={UpdatedAt}",
                _metadata.Version,
                _metadata.DefinitionCount,
                _metadata.UpdatedBy,
                _metadata.UpdatedAt);
        }
        catch (Exception ex)
        {
            _log.LogCritical(ex, "Failed to load Town Project config for realm {RealmId}. Startup will fail.", _realmId);
            throw;
        }
    }

    public IReadOnlyDictionary<string, TownProjectDefinition> GetAllDefinitions()
        => _config.Definitions;

    public IReadOnlyDictionary<string, TownProjectRequirementPool> GetAllRequirementPools()
        => _config.RequirementPools;

    public IReadOnlyDictionary<string, TownProjectItemGroup> GetAllItemGroups()
        => _config.ItemGroups;

    public TownProjectDefinition? GetDefinition(string definitionId)
        => _config.Definitions.TryGetValue(definitionId, out var def) ? def : null;

    public string GetConfigVersion()
        => _config.Version;

    public TownProjectConfigMetadata GetConfigMetadata()
        => _metadata;

    protected override string? GetRealmVersion(RealmProjectConfigRef? realmRef)
        => realmRef?.ProjectsVersion;

    protected override RealmProjectConfigRef CreateRealmRef(string version)
        => new() { ProjectsVersion = version };

    protected override string GetVersion(TownProjectConfigRoot config)
        => config.Version;

    protected override DateTime GetUpdatedAt(TownProjectConfigRoot config)
        => config.UpdatedAt;

    protected override string GetUpdatedBy(TownProjectConfigRoot config)
        => config.UpdatedBy;

    protected override IReadOnlyDictionary<string, TownProjectDefinition> GetDefinitions(TownProjectConfigRoot config)
        => config.Definitions;

    protected override bool DefinitionEquals(TownProjectDefinition left, TownProjectDefinition right)
    {
        return JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    }

    protected override ConfigValidationResult Validate(TownProjectConfigRoot config)
    {
        var result = new ConfigValidationResult();

        void AddError(string code, string message) => result.Issues.Add(new ConfigValidationIssue
        {
            Severity = ConfigValidationSeverity.Error,
            Code = code,
            Message = message
        });

        void AddWarning(string code, string message) => result.Issues.Add(new ConfigValidationIssue
        {
            Severity = ConfigValidationSeverity.Warning,
            Code = code,
            Message = message
        });

        if (string.IsNullOrWhiteSpace(config.Version))
            AddError("version.missing", "Config version is empty or null.");

        if (config.UpdatedAt == default)
            AddWarning("metadata.updatedAt.missing", "UpdatedAt is missing or default.");

        if (string.IsNullOrWhiteSpace(config.UpdatedBy))
            AddWarning("metadata.updatedBy.missing", "UpdatedBy is empty.");

        var definitions = config.Definitions ?? new Dictionary<string, TownProjectDefinition>();
        var requirementPools = config.RequirementPools ?? new Dictionary<string, TownProjectRequirementPool>();
        var itemGroups = config.ItemGroups ?? new Dictionary<string, TownProjectItemGroup>();

        if (definitions.Count == 0)
            AddError("definitions.empty", "Config has no definitions.");

        if (requirementPools.Count == 0)
            AddError("requirementPools.empty", "Config has no requirement pools.");

        var seenGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (groupKey, group) in itemGroups)
        {
            var groupPrefix = $"ItemGroup '{groupKey}'";
            if (group == null)
            {
                AddError("itemGroup.null", $"{groupPrefix}: Group payload is null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(group.Id))
                AddError("itemGroup.id.missing", $"{groupPrefix}: Id is empty.");
            else if (!seenGroupIds.Add(group.Id))
                AddError("itemGroup.id.duplicate", $"{groupPrefix}: Duplicate item group ID '{group.Id}'.");

            if (!string.IsNullOrWhiteSpace(group.Id) && !string.Equals(groupKey, group.Id, StringComparison.Ordinal))
                AddError("itemGroup.id.key_mismatch", $"{groupPrefix}: Key '{groupKey}' does not match group Id '{group.Id}'.");

            if (group.ItemIds == null || group.ItemIds.Count == 0)
            {
                AddError("itemGroup.items.empty", $"{groupPrefix}: ItemIds must contain at least one item.");
                continue;
            }

            var duplicateGroupItems = group.ItemIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateGroupItems.Count > 0)
                AddError("itemGroup.items.duplicate", $"{groupPrefix}: Duplicate ItemIds found ({string.Join(", ", duplicateGroupItems)}).");

            var hasInvalidGroupItem = false;
            foreach (var itemId in group.ItemIds)
            {
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    hasInvalidGroupItem = true;
                    break;
                }
            }

            if (hasInvalidGroupItem)
                AddError("itemGroup.items.invalid", $"{groupPrefix}: ItemIds contains empty values.");
        }

        var seenPoolIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (poolKey, pool) in requirementPools)
        {
            var poolPrefix = $"RequirementPool '{poolKey}'";
            if (pool == null)
            {
                AddError("requirementPool.null", $"{poolPrefix}: Pool payload is null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(pool.Id))
                AddError("requirementPool.id.missing", $"{poolPrefix}: Id is empty.");
            else if (!seenPoolIds.Add(pool.Id))
                AddError("requirementPool.id.duplicate", $"{poolPrefix}: Duplicate requirement pool ID '{pool.Id}'.");

            if (!string.IsNullOrWhiteSpace(pool.Id) && !string.Equals(poolKey, pool.Id, StringComparison.Ordinal))
                AddError("requirementPool.id.key_mismatch", $"{poolPrefix}: Key '{poolKey}' does not match pool Id '{pool.Id}'.");

            if (pool.SelectionMode == TownProjectRequirementSelectionMode.Unknown)
                AddError("requirementPool.selectionMode.unknown", $"{poolPrefix}: SelectionMode is Unknown.");

            if (pool.RecentHistorySize < 0)
                AddError("requirementPool.history.invalid", $"{poolPrefix}: RecentHistorySize must be >= 0.");

            if (pool.Entries == null || pool.Entries.Count == 0)
            {
                AddError("requirementPool.entries.empty", $"{poolPrefix}: Pool has no entries.");
                continue;
            }

            var seenEntryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in pool.Entries)
            {
                if (entry == null)
                {
                    AddError("requirementEntry.null", $"{poolPrefix}: Entry payload is null.");
                    continue;
                }

                var entryPrefix = $"{poolPrefix}, entry '{entry.Id}'";
                if (string.IsNullOrWhiteSpace(entry.Id))
                    AddError("requirementEntry.id.missing", $"{poolPrefix}: Entry Id is empty.");
                else if (!seenEntryIds.Add(entry.Id))
                    AddError("requirementEntry.id.duplicate", $"{poolPrefix}: Duplicate entry ID '{entry.Id}'.");

                if (entry.Weight <= 0)
                    AddError("requirementEntry.weight.invalid", $"{entryPrefix}: Weight must be > 0.");

                if (entry.ContributionType == TownProjectContributionType.Unknown)
                    AddError("requirementEntry.contributionType.unknown", $"{entryPrefix}: ContributionType is Unknown.");

                if (entry.TargetQuantity <= 0)
                    AddError("requirementEntry.target.invalid", $"{entryPrefix}: TargetQuantity must be > 0.");

                if (entry.ProgressPerUnit <= 0)
                    AddError("requirementEntry.progressPerUnit.invalid", $"{entryPrefix}: ProgressPerUnit must be > 0.");

                if (entry.ReputationPerUnit < 0)
                    AddError("requirementEntry.reputationPerUnit.invalid", $"{entryPrefix}: ReputationPerUnit must be >= 0.");

                if (entry.ContributionType == TownProjectContributionType.ItemDelivery)
                {
                    var hasIds = entry.AllowedItemIds != null && entry.AllowedItemIds.Count > 0;
                    var hasGroup = !string.IsNullOrWhiteSpace(entry.AllowedItemGroupId);
                    if (!hasIds && !hasGroup)
                        AddError("requirementEntry.items.missing", $"{entryPrefix}: ItemDelivery entries must define AllowedItemIds and/or AllowedItemGroupId.");

                    if (hasIds)
                    {
                        var hasInvalidItemId = false;
                        foreach (var itemId in entry.AllowedItemIds ?? new List<string>())
                        {
                            if (string.IsNullOrWhiteSpace(itemId))
                            {
                                hasInvalidItemId = true;
                                break;
                            }
                        }

                        if (hasInvalidItemId)
                            AddError("requirementEntry.items.invalid", $"{entryPrefix}: AllowedItemIds contains empty values.");
                    }

                    if (hasGroup && !itemGroups.ContainsKey(entry.AllowedItemGroupId!))
                        AddError("requirementEntry.group.missing", $"{entryPrefix}: AllowedItemGroupId '{entry.AllowedItemGroupId}' was not found.");

                    ValidateQualityRule(entryPrefix, entry.QualityRule, AddError);
                    ValidateConditionRule(entryPrefix, entry.ConditionRule, AddError);
                }
                else
                {
                    if ((entry.AllowedItemIds?.Count ?? 0) > 0 || !string.IsNullOrWhiteSpace(entry.AllowedItemGroupId))
                        AddWarning("requirementEntry.items.unused", $"{entryPrefix}: Item filters are set but entry is not ItemDelivery.");
                }
            }
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, def) in definitions)
        {
            var prefix = $"Definition '{key}'";

            if (def == null)
            {
                AddError("definition.null", $"{prefix}: Definition payload is null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(def.Id))
                AddError("definition.id.missing", $"{prefix}: Id is empty.");
            else if (!seenIds.Add(def.Id))
                AddError("definition.id.duplicate", $"{prefix}: Duplicate definition ID '{def.Id}'.");

            if (!string.IsNullOrEmpty(def.Id) && key != def.Id)
                AddError("definition.id.key_mismatch", $"{prefix}: Key '{key}' does not match definition Id '{def.Id}'.");

            if (string.IsNullOrWhiteSpace(def.Name))
                AddError("definition.name.missing", $"{prefix}: Name is empty.");

            if (string.IsNullOrWhiteSpace(def.Description))
                AddWarning("definition.description.missing", $"{prefix}: Description is empty.");

            if (def.Category == TownProjectCategory.Unknown)
                AddError("definition.category.unknown", $"{prefix}: Category is Unknown.");

            if (string.IsNullOrWhiteSpace(def.RequirementPoolId))
            {
                AddError("definition.requirementPoolId.missing", $"{prefix}: RequirementPoolId is empty.");
            }
            else if (!requirementPools.ContainsKey(def.RequirementPoolId))
            {
                AddError("definition.requirementPoolId.unknown", $"{prefix}: RequirementPoolId '{def.RequirementPoolId}' not found.");
            }

            if (def.RewardType == TownProjectRewardType.Unknown)
                AddError("definition.rewardType.unknown", $"{prefix}: RewardType is Unknown.");

            if (def.RewardScope == TownProjectRewardScope.Unknown)
                AddError("definition.rewardScope.unknown", $"{prefix}: RewardScope is Unknown.");

            if (string.IsNullOrWhiteSpace(def.RewardValue))
                AddError("definition.rewardValue.missing", $"{prefix}: RewardValue is empty.");

            if (def.RewardDurationSeconds < 0)
                AddError("definition.rewardDuration.invalid", $"{prefix}: RewardDurationSeconds must be >= 0.");

            if (def.EventType == TownProjectEventType.Unknown)
                AddError("definition.eventType.unknown", $"{prefix}: EventType is Unknown.");

            if (!def.IsEnabled)
                AddWarning("definition.disabled", $"{prefix}: IsEnabled is false.");
        }

        return result;
    }

    private static void ValidateQualityRule(string entryPrefix, ItemQualityRule? rule, Action<string, string> addError)
    {
        if (rule == null)
            return;

        if (rule.Mode == ItemQualityRuleMode.None)
            return;

        if (rule.Mode != ItemQualityRuleMode.Exact && rule.Mode != ItemQualityRuleMode.Minimum)
        {
            addError("requirementEntry.quality.mode.invalid", $"{entryPrefix}: QualityRule mode is invalid.");
            return;
        }

        if (rule.Quality == TownProjectItemQuality.Unknown)
            addError("requirementEntry.quality.value.missing", $"{entryPrefix}: QualityRule requires a non-Unknown quality value.");
    }

    private static void ValidateConditionRule(string entryPrefix, ItemConditionRule? rule, Action<string, string> addError)
    {
        if (rule == null)
            return;

        switch (rule.Mode)
        {
            case ItemConditionRuleMode.Any:
            case ItemConditionRuleMode.PristineOnly:
                return;
            case ItemConditionRuleMode.ExactEndurance:
                if (rule.ExactEndurance < 0)
                    addError("requirementEntry.condition.exact.invalid", $"{entryPrefix}: ExactEndurance must be >= 0.");
                return;
            case ItemConditionRuleMode.MinimumEndurance:
                if (rule.MinimumEndurance < 0)
                    addError("requirementEntry.condition.minimum.invalid", $"{entryPrefix}: MinimumEndurance must be >= 0.");
                return;
            case ItemConditionRuleMode.MinimumEndurancePercent:
                if (rule.MinimumEndurancePercent < 0 || rule.MinimumEndurancePercent > 100)
                    addError("requirementEntry.condition.minimumPercent.invalid", $"{entryPrefix}: MinimumEndurancePercent must be in [0, 100].");
                return;
            default:
                addError("requirementEntry.condition.mode.invalid", $"{entryPrefix}: ConditionRule mode is invalid.");
                return;
        }
    }

    private TownProjectConfigRoot BootstrapDefaults(ITownProjectConfigRepository repository, string realmId)
    {
        var now = DateTime.UtcNow;
        var version = $"default";

        var defaults = BuildDefaultConfig(version, now);
        var validation = Validate(defaults);

        if (!validation.IsValid)
        {
            var message = "Built-in Town Project bootstrap defaults are invalid: " +
                          string.Join("; ", validation.Issues.Select(i => $"[{i.Severity}] {i.Code}: {i.Message}"));
            throw new InvalidOperationException(message);
        }

        repository.SaveConfigVersionAsync(version, defaults, CancellationToken.None)
            .GetAwaiter().GetResult();

        repository.SaveRealmConfigRefAsync(realmId, new RealmProjectConfigRef
        {
            ProjectsVersion = version
        }, CancellationToken.None)
            .GetAwaiter().GetResult();

        _log.LogWarning(
            "Bootstrapped Town Project defaults for realm {RealmId} as version {Version}.",
            realmId,
            version);

        return defaults;
    }

    private static TownProjectConfigRoot BuildDefaultConfig(string version, DateTime now)
    {
        var itemGroups = new Dictionary<string, TownProjectItemGroup>(StringComparer.OrdinalIgnoreCase)
        {
            ["common-herbs"] = new TownProjectItemGroup
            {
                Id = "common-herbs",
                Name = "Common Herbs",
                ItemIds = ["briar-flower", "mushroom", "flax"]
            },
            ["reinforcement-armor"] = new TownProjectItemGroup
            {
                Id = "reinforcement-armor",
                Name = "Reinforcement Armor",
                ItemIds = ["plate-mail", "plate-leggings"]
            }
        };

        var requirementPools = new Dictionary<string, TownProjectRequirementPool>(StringComparer.OrdinalIgnoreCase)
        {
            ["weekly-hunt-basic-pool"] = new TownProjectRequirementPool
            {
                Id = "weekly-hunt-basic-pool",
                SelectionMode = TownProjectRequirementSelectionMode.WeightedRandom,
                PreventImmediateRepeat = true,
                RecentHistorySize = 1,
                Entries =
                [
                    new TownProjectRequirementEntry
                    {
                        Id = "weekly-hunt-basic-kills",
                        Weight = 1,
                        ContributionType = TownProjectContributionType.MonsterKill,
                        TargetQuantity = 1000,
                        ProgressPerUnit = 1,
                        ReputationPerUnit = 1
                    }
                ]
            },
            ["crusade-prep-basic-pool"] = new TownProjectRequirementPool
            {
                Id = "crusade-prep-basic-pool",
                SelectionMode = TownProjectRequirementSelectionMode.WeightedRandom,
                PreventImmediateRepeat = true,
                RecentHistorySize = 1,
                Entries =
                [
                    new TownProjectRequirementEntry
                    {
                        Id = "crusade-plate-mail-sturdy-min",
                        Weight = 1,
                        ContributionType = TownProjectContributionType.ItemDelivery,
                        TargetQuantity = 200,
                        ProgressPerUnit = 1,
                        ReputationPerUnit = 2,
                        AllowedItemIds = ["plate-mail"],
                        QualityRule = new ItemQualityRule
                        {
                            Mode = ItemQualityRuleMode.Minimum,
                            Quality = TownProjectItemQuality.Sturdy
                        }
                    },
                    new TownProjectRequirementEntry
                    {
                        Id = "crusade-reinforcement-armor-sturdy-min",
                        Weight = 1,
                        ContributionType = TownProjectContributionType.ItemDelivery,
                        TargetQuantity = 200,
                        ProgressPerUnit = 1,
                        ReputationPerUnit = 2,
                        AllowedItemGroupId = "reinforcement-armor",
                        QualityRule = new ItemQualityRule
                        {
                            Mode = ItemQualityRuleMode.Minimum,
                            Quality = TownProjectItemQuality.Sturdy
                        }
                    }
                ]
            }
        };

        var definitions = new Dictionary<string, TownProjectDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["weekly-hunt-basic"] = new TownProjectDefinition
            {
                Id = "weekly-hunt-basic",
                Name = "Weekly Hunt Drive",
                Description = "Defeat monsters across the realm to reinforce town defenses.",
                Category = TownProjectCategory.WeeklyGeneral,
                RequirementPoolId = "weekly-hunt-basic-pool",
                RewardType = TownProjectRewardType.Buff,
                RewardScope = TownProjectRewardScope.Town,
                RewardValue = "town.damage.multiplier=1.05;town.regen.multiplier=1.05",
                RewardDurationSeconds = 604800,
                EventType = TownProjectEventType.WeeklyReset,
                IsEnabled = true,
                IndividualRewardMode = TownProjectIndividualRewardMode.AllCitizens
            },
            ["crusade-prep-basic"] = new TownProjectDefinition
            {
                Id = "crusade-prep-basic",
                Name = "Crusade Preparation",
                Description = "Deliver supplies before crusade to strengthen fortifications.",
                Category = TownProjectCategory.CrusadePreparation,
                RequirementPoolId = "crusade-prep-basic-pool",
                RewardType = TownProjectRewardType.Buff,
                RewardScope = TownProjectRewardScope.Town,
                RewardValue = "crusade.mana.shield.count.flat=1;crusade.construction.points.flat=2",
                RewardDurationSeconds = 14400,
                EventType = TownProjectEventType.CrusadeStart,
                IsEnabled = true,
                IndividualRewardMode = TownProjectIndividualRewardMode.AllCitizens
            }
        };

        return new TownProjectConfigRoot
        {
            Version = version,
            UpdatedAt = now,
            UpdatedBy = "system-bootstrap",
            PublishedAt = null,
            PublishedBy = null,
            Definitions = definitions,
            RequirementPools = requirementPools,
            ItemGroups = itemGroups
        };
    }
}