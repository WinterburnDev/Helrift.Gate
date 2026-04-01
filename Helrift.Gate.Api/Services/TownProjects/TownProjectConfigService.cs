// Services/TownProjects/TownProjectConfigService.cs
using Helrift.Gate.Api.Services.TownProjects;
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts.TownProjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Helrift.Gate.Api.Services.TownProjects;

public sealed class TownProjectConfigService : ITownProjectConfigService
{
    private readonly ILogger<TownProjectConfigService> _log;
    private readonly string _realmId;
    private readonly TownProjectConfigRoot _config;
    private readonly TownProjectConfigMetadata _metadata;

    public TownProjectConfigService(
        ILogger<TownProjectConfigService> log,
        IConfiguration configuration,
        ITownProjectConfigRepository repository)
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
                throw new InvalidOperationException(
                    $"Town Project config not found for realm '{_realmId}'. " +
                    $"Expected path: /realms/{_realmId}/config with 'projectsVersion' field.");
            }

            var version = realmConfigRef.ProjectsVersion;
            _log.LogInformation("Realm {RealmId} uses Town Project config version: {Version}", _realmId, version);

            // Load versioned config
            var config = repository.GetConfigVersionAsync(version, CancellationToken.None)
                .GetAwaiter().GetResult();

            if (config == null || config.Definitions == null)
            {
                throw new InvalidOperationException(
                    $"Town Project config version '{version}' not found or invalid. " +
                    $"Expected path: /config/projects/versions/{version}");
            }

            _config = config;

            // Validate config
            ValidateConfig(_config);

            // Build metadata
            _metadata = new TownProjectConfigMetadata
            {
                RealmId = _realmId,
                Version = _config.Version,
                UpdatedAt = _config.UpdatedAt,
                UpdatedBy = _config.UpdatedBy,
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

    private void ValidateConfig(TownProjectConfigRoot config)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.Version))
            errors.Add("Config version is empty or null.");

        if (config.Definitions == null || config.Definitions.Count == 0)
            errors.Add("Config has no definitions.");

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, def) in config.Definitions)
        {
            var prefix = $"Definition '{key}'";

            if (string.IsNullOrWhiteSpace(def.Id))
                errors.Add($"{prefix}: Id is empty.");
            else if (!seenIds.Add(def.Id))
                errors.Add($"{prefix}: Duplicate definition ID '{def.Id}'.");

            if (!string.IsNullOrEmpty(def.Id) && key != def.Id)
                errors.Add($"{prefix}: Key '{key}' does not match definition Id '{def.Id}'.");

            if (string.IsNullOrWhiteSpace(def.Name))
                errors.Add($"{prefix}: Name is empty.");

            if (def.Category == TownProjectCategory.Unknown)
                errors.Add($"{prefix}: Category is Unknown.");

            if (def.ContributionType == TownProjectContributionType.Unknown)
                errors.Add($"{prefix}: ContributionType is Unknown.");

            if (def.ContributionType == TownProjectContributionType.ItemDelivery &&
                string.IsNullOrWhiteSpace(def.RequiredItemId))
                errors.Add($"{prefix}: ContributionType is ItemDelivery but RequiredItemId is empty.");

            if (def.TargetProgress <= 0)
                errors.Add($"{prefix}: TargetProgress must be > 0.");

            if (def.ProgressPerContributionUnit <= 0)
                errors.Add($"{prefix}: ProgressPerContributionUnit must be > 0.");

            if (def.ReputationPerContributionUnit < 0)
                errors.Add($"{prefix}: ReputationPerContributionUnit must be >= 0.");

            if (def.RewardType == TownProjectRewardType.Unknown)
                errors.Add($"{prefix}: RewardType is Unknown.");

            if (def.RewardScope == TownProjectRewardScope.Unknown)
                errors.Add($"{prefix}: RewardScope is Unknown.");

            if (string.IsNullOrWhiteSpace(def.RewardValue))
                errors.Add($"{prefix}: RewardValue is empty.");

            if (def.RewardDurationSeconds < 0)
                errors.Add($"{prefix}: RewardDurationSeconds must be >= 0.");

            if (def.EventType == TownProjectEventType.Unknown)
                errors.Add($"{prefix}: EventType is Unknown.");
        }

        if (errors.Any())
        {
            var message = $"Town Project config validation failed with {errors.Count} error(s):\n" +
                          string.Join("\n", errors.Select(e => $"  - {e}"));
            throw new InvalidOperationException(message);
        }
    }
}