// App/Repositories/ITownProjectConfigRepository.cs
using Helrift.Gate.Contracts.TownProjects;

namespace Helrift.Gate.App.Repositories;

/// <summary>
/// Repository for loading Town Project configuration from Firebase.
/// </summary>
public interface ITownProjectConfigRepository
{
    /// <summary>
    /// Get the realm's project config reference (which version to use).
    /// Path: /realms/{realmId}/config
    /// </summary>
    Task<RealmProjectConfigRef?> GetRealmConfigRefAsync(string realmId, CancellationToken ct = default);

    /// <summary>
    /// Get the project config root for a specific version.
    /// Path: /config/projects/versions/{version}
    /// </summary>
    Task<TownProjectConfigRoot?> GetConfigVersionAsync(string version, CancellationToken ct = default);
}