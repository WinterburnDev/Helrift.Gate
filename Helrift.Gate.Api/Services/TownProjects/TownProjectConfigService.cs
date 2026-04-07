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

            if (config == null || config.Definitions == null)
            {
                _log.LogWarning(
                    "Town Project config version {Version} was selected for realm {RealmId} but not found. Bootstrapping defaults.",
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
                "Town Project config loaded successfully: Version={Version}, Definitions={Count}, Pools={Pools}, Groups={Groups}, UpdatedBy={UpdatedBy}, UpdatedAt={UpdatedAt}",
                _metadata.Version,
                _metadata.DefinitionCount,
                _config.RequirementPools.Count,
                _config.ItemGroups.Count,
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

    public TownProjectDefinition? GetDefinition(string definitionId)
        => _config.Definitions.TryGetValue(definitionId, out var def) ? def : null;

    public TownProjectRequirementPool? GetRequirementPool(string poolId)
        => _config.RequirementPools.TryGetValue(poolId, out var pool) ? pool : null;

    public TownProjectItemGroup? GetItemGroup(string groupId)
        => _config.ItemGroups.TryGetValue(groupId, out var group) ? group : null;

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
        var pools = config.RequirementPools ?? new Dictionary<string, TownProjectRequirementPool>();
        var groups = config.ItemGroups ?? new Dictionary<string, TownProjectItemGroup>();

        // ---- Validate item groups ----

        var seenGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (groupKey, group) in groups)
        {
            var gPrefix = $"ItemGroup '{groupKey}'";

            if (group == null)
            {
                AddError("itemGroup.null", $"{gPrefix}: Group payload is null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(group.Id))
                AddError("itemGroup.id.missing", $"{gPrefix}: Id is empty.");
            else if (!seenGroupIds.Add(group.Id))
                AddError("itemGroup.id.duplicate", $"{gPrefix}: Duplicate group ID '{group.Id}'.");

            if (!string.IsNullOrEmpty(group.Id) && !string.Equals(groupKey, group.Id, StringComparison.OrdinalIgnoreCase))
                AddError("itemGroup.id.key_mismatch", $"{gPrefix}: Key '{groupKey}' does not match group Id '{group.Id}'.");

            if (string.IsNullOrWhiteSpace(group.Name))
                AddWarning("itemGroup.name.missing", $"{gPrefix}: Name is empty.");

            if (group.ItemIds == null || group.ItemIds.Count == 0)
                AddError("itemGroup.itemIds.empty", $"{gPrefix}: ItemIds list is empty or null.");
        }

        // ---- Validate requirement pools ----

        var seenPoolIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (poolKey, pool) in pools)
        {
            var pPrefix = $"RequirementPool '{poolKey}'";

            if (pool == null)
            {
                AddError("pool.null", $"{pPrefix}: Pool payload is null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(pool.Id))
                AddError("pool.id.missing", $"{pPrefix}: Id is empty.");
            else if (!seenPoolIds.Add(pool.Id))
                AddError("pool.id.duplicate", $"{pPrefix}: Duplicate pool ID '{pool.Id}'.");

            if (!string.IsNullOrEmpty(pool.Id) && !string.Equals(poolKey, pool.Id, StringComparison.OrdinalIgnoreCase))
                AddError("pool.id.key_mismatch", $"{pPrefix}: Key '{poolKey}' does not match pool Id '{pool.Id}'.");

            if (pool.Entries == null || pool.Entries.Count == 0)
            {
                AddError("pool.entries.empty", $"{pPrefix}: Pool has no entries. At least one entry is required.");
                continue;
            }

            var seenEntryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in pool.Entries)
            {
                var ePrefix = $"{pPrefix}, Entry '{entry?.Id ?? "<null>"}'";

                if (entry == null)
                {
                    AddError("pool.entry.null", $"{pPrefix}: An entry in the pool is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.Id))
                    AddError("pool.entry.id.missing", $"{ePrefix}: Entry Id is empty.");
                else if (!seenEntryIds.Add(entry.Id))
                    AddError("pool.entry.id.duplicate", $"{ePrefix}: Duplicate entry ID '{entry.Id}' within pool '{pool.Id}'.");

                if (entry.Weight < 1)
                    AddError("pool.entry.weight.invalid", $"{ePrefix}: Weight must be >= 1 (got {entry.Weight}).");

                if (entry.ContributionType == TownProjectContributionType.Unknown)
                    AddError("pool.entry.contributionType.unknown", $"{ePrefix}: ContributionType is Unknown.");

                if (entry.TargetQuantity <= 0)
                    AddError("pool.entry.targetQuantity.invalid", $"{ePrefix}: TargetQuantity must be > 0.");

                if (entry.ProgressPerUnit <= 0)
                    AddError("pool.entry.progressPerUnit.invalid", $"{ePrefix}: ProgressPerUnit must be > 0.");

                if (entry.ReputationPerUnit < 0)
                    AddError("pool.entry.reputationPerUnit.invalid", $"{ePrefix}: ReputationPerUnit must be >= 0.");

                if (entry.ContributionType == TownProjectContributionType.ItemDelivery)
                {
                    var hasItemIds = entry.AllowedItemIds != null && entry.AllowedItemIds.Count > 0;
                    var hasGroupId = !string.IsNullOrWhiteSpace(entry.AllowedItemGroupId);

                    if (!hasItemIds && !hasGroupId)
                        AddError("pool.entry.items.missing",
                            $"{ePrefix}: ItemDelivery entry must specify AllowedItemIds or AllowedItemGroupId.");

                    if (hasItemIds && hasGroupId)
                        AddError("pool.entry.items.conflict",
                            $"{ePrefix}: AllowedItemIds and AllowedItemGroupId are mutually exclusive; set only one.");

                    if (hasGroupId && !groups.ContainsKey(entry.AllowedItemGroupId!))
                        AddError("pool.entry.groupId.notFound",
                            $"{ePrefix}: AllowedItemGroupId '{entry.AllowedItemGroupId}' references an unknown item group.");

                    ValidateQualityRule(entry.QualityRule, ePrefix, AddError);
                    ValidateConditionRule(entry.ConditionRule, ePrefix, AddError);
                }

                if (entry.ContributionType != TownProjectContributionType.ItemDelivery)
                {
                    if (entry.AllowedItemIds != null && entry.AllowedItemIds.Count > 0)
                        AddWarning("pool.entry.items.unexpected",
                            $"{ePrefix}: AllowedItemIds is set but ContributionType is not ItemDelivery.");

                    if (!string.IsNullOrWhiteSpace(entry.AllowedItemGroupId))
                        AddWarning("pool.entry.groupId.unexpected",
                            $"{ePrefix}: AllowedItemGroupId is set but ContributionType is not ItemDelivery.");
                }
            }
        }

        // ---- Validate definitions ----

        if (definitions.Count == 0)
            AddError("definitions.empty", "Config has no definitions.");

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

            var usesPool = !string.IsNullOrWhiteSpace(def.RequirementPoolId);

            if (usesPool)
            {
                // Pool-based definition: validate the referenced pool exists
                if (!pools.ContainsKey(def.RequirementPoolId!))
                    AddError("definition.requirementPoolId.notFound",
                        $"{prefix}: RequirementPoolId '{def.RequirementPoolId}' references an unknown pool.");
            }
            else
            {
                // Legacy flat definition: validate the legacy fields
                if (def.ContributionType == TownProjectContributionType.Unknown)
                    AddError("definition.contributionType.unknown", $"{prefix}: ContributionType is Unknown.");

                if (def.ContributionType == TownProjectContributionType.ItemDelivery &&
                    string.IsNullOrWhiteSpace(def.RequiredItemId))
                    AddError("definition.requiredItemId.missing",
                        $"{prefix}: ContributionType is ItemDelivery but RequiredItemId is empty (and no RequirementPoolId is set).");

                if (def.TargetProgress <= 0)
                    AddError("definition.targetProgress.invalid", $"{prefix}: TargetProgress must be > 0.");

                if (def.ProgressPerContributionUnit <= 0)
                    AddError("definition.progressPerUnit.invalid", $"{prefix}: ProgressPerContributionUnit must be > 0.");

                if (def.ReputationPerContributionUnit < 0)
                    AddError("definition.reputation.invalid", $"{prefix}: ReputationPerContributionUnit must be >= 0.");
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

    private static void ValidateQualityRule(ItemQualityRule? rule, string entryPrefix, Action<string, string> addError)
    {
        if (rule == null || rule.Mode == ItemQualityRuleMode.None)
            return;

        if (string.IsNullOrWhiteSpace(rule.QualityValue))
            addError("pool.entry.qualityRule.value.missing",
                $"{entryPrefix}: QualityRule Mode is {rule.Mode} but QualityValue is empty.");
    }

    private static void ValidateConditionRule(ItemConditionRule? rule, string entryPrefix, Action<string, string> addError)
    {
        if (rule == null || rule.Mode == ItemConditionRuleMode.Any)
            return;

        if (rule.Threshold == null)
            addError("pool.entry.conditionRule.threshold.missing",
                $"{entryPrefix}: ConditionRule Mode is {rule.Mode} but Threshold is null.");
        else if (rule.Threshold < 0f || rule.Threshold > 1f)
            addError("pool.entry.conditionRule.threshold.range",
                $"{entryPrefix}: ConditionRule Threshold must be in [0.0, 1.0] (got {rule.Threshold}).");
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
        // ---- Item groups ----
        var itemGroups = new Dictionary<string, TownProjectItemGroup>(StringComparer.OrdinalIgnoreCase)
        {
            ["common_herbs"] = new TownProjectItemGroup
            {
                Id = "common_herbs",
                Name = "Common Herbs",
                ItemIds = new List<string> { "briar_flower", "mushroom", "flax" }
            }
        };

        // ---- Requirement pools ----
        var requirementPools = new Dictionary<string, TownProjectRequirementPool>(StringComparer.OrdinalIgnoreCase)
        {
            ["crusade-prep-pool"] = new TownProjectRequirementPool
            {
                Id = "crusade-prep-pool",
                SelectionMode = RequirementPoolSelectionMode.WeightedRandom,
                PreventImmediateRepeat = true,
                Entries = new List<TownProjectRequirementEntry>
                {
                    new()
                    {
                        Id = "crusade-prep-mana-shard",
                        Weight = 1,
                        ContributionType = TownProjectContributionType.ItemDelivery,
                        TargetQuantity = 200,
                        ProgressPerUnit = 1,
                        ReputationPerUnit = 2,
                        AllowedItemIds = new List<string> { "mana-shard" }
                    },
                    new()
                    {
                        Id = "crusade-prep-herbs",
                        Weight = 1,
                        ContributionType = TownProjectContributionType.ItemDelivery,
                        TargetQuantity = 300,
                        ProgressPerUnit = 1,
                        ReputationPerUnit = 1,
                        AllowedItemGroupId = "common_herbs"
                    }
                }
            }
        };

        // ---- Definitions ----
        var definitions = new Dictionary<string, TownProjectDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["weekly-hunt-basic"] = new TownProjectDefinition
            {
                Id = "weekly-hunt-basic",
                Name = "Weekly Hunt Drive",
                Description = "Defeat monsters across the realm to reinforce town defenses.",
                Category = TownProjectCategory.WeeklyGeneral,
                ContributionType = TownProjectContributionType.MonsterKill,
                RequiredItemId = null,
                TargetProgress = 1000,
                ProgressPerContributionUnit = 1,
                ReputationPerContributionUnit = 1,
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
                RequirementPoolId = "crusade-prep-pool",
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
