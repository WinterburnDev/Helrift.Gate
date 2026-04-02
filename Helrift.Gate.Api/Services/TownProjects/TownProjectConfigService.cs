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

            if (def.ContributionType == TownProjectContributionType.Unknown)
                AddError("definition.contributionType.unknown", $"{prefix}: ContributionType is Unknown.");

            if (def.ContributionType == TownProjectContributionType.ItemDelivery &&
                string.IsNullOrWhiteSpace(def.RequiredItemId))
                AddError("definition.requiredItemId.missing", $"{prefix}: ContributionType is ItemDelivery but RequiredItemId is empty.");

            if (def.TargetProgress <= 0)
                AddError("definition.targetProgress.invalid", $"{prefix}: TargetProgress must be > 0.");

            if (def.ProgressPerContributionUnit <= 0)
                AddError("definition.progressPerUnit.invalid", $"{prefix}: ProgressPerContributionUnit must be > 0.");

            if (def.ReputationPerContributionUnit < 0)
                AddError("definition.reputation.invalid", $"{prefix}: ReputationPerContributionUnit must be >= 0.");

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
                ContributionType = TownProjectContributionType.ItemDelivery,
                RequiredItemId = "mana-shard",
                TargetProgress = 200,
                ProgressPerContributionUnit = 1,
                ReputationPerContributionUnit = 2,
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
            Definitions = definitions
        };
    }
}