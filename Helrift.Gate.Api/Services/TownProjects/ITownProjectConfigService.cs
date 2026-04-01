// Services/TownProjects/ITownProjectConfigService.cs
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
}