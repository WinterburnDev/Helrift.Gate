// Services/TownProjects/ITownProjectConfigService.cs
using Helrift.Gate.Api.Services.ConfigPlatform;
using Helrift.Gate.Contracts.TownProjects;

namespace Helrift.Gate.Api.Services.TownProjects;

/// <summary>
/// Service for loading, validating, and caching Town Project configuration.
/// Config is loaded once at startup and cached for the lifetime of the process.
/// </summary>
public interface ITownProjectConfigService
{
    /// <summary>
    /// Get all project definitions.
    /// </summary>
    IReadOnlyDictionary<string, TownProjectDefinition> GetAllDefinitions();

    /// <summary>
    /// Get a specific project definition by ID.
    /// </summary>
    TownProjectDefinition? GetDefinition(string definitionId);

    /// <summary>
    /// Get the current config version string.
    /// </summary>
    string GetConfigVersion();

    /// <summary>
    /// Get metadata about the loaded config.
    /// </summary>
    TownProjectConfigMetadata GetConfigMetadata();

    Task<IReadOnlyList<ConfigVersionSummary>> ListVersionsAsync(CancellationToken ct = default);
    Task<TownProjectConfigRoot?> GetVersionAsync(string version, CancellationToken ct = default);
    Task<ConfigCompareResult> CompareVersionsAsync(string leftVersion, string rightVersion, CancellationToken ct = default);

    Task<IReadOnlyList<RealmVersionSelection>> ListRealmSelectionsAsync(CancellationToken ct = default);
    Task<string?> GetRealmSelectionAsync(string realmId, CancellationToken ct = default);
    Task SetRealmSelectionAsync(string realmId, string version, CancellationToken ct = default);

    Task<ConfigValidationResult> ValidateAsync(TownProjectConfigRoot config, CancellationToken ct = default);
    Task<ConfigSaveResult> SaveVersionAsync(TownProjectConfigRoot config, CancellationToken ct = default);
}