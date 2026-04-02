namespace Helrift.Gate.App.Repositories;

public sealed record ConfigVersionRecord<TConfig>
{
    public string StorageKey { get; init; } = string.Empty;
    public TConfig Config { get; init; } = default!;
}

public sealed record RealmConfigRefRecord<TRealmRef>
{
    public string RealmId { get; init; } = string.Empty;
    public TRealmRef ConfigRef { get; init; } = default!;
}

/// <summary>
/// Generic repository abstraction for versioned config domains.
/// Town Projects uses this now; future domains (items, NPCs, mobs, events) can reuse it.
/// </summary>
public interface IVersionedConfigRepository<TConfig, TRealmRef>
{
    Task<TRealmRef?> GetRealmConfigRefAsync(string realmId, CancellationToken ct = default);
    Task SaveRealmConfigRefAsync(string realmId, TRealmRef realmRef, CancellationToken ct = default);

    Task<TConfig?> GetConfigVersionAsync(string version, CancellationToken ct = default);
    Task SaveConfigVersionAsync(string version, TConfig config, CancellationToken ct = default);

    Task<IReadOnlyList<ConfigVersionRecord<TConfig>>> ListConfigVersionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RealmConfigRefRecord<TRealmRef>>> ListRealmConfigRefsAsync(CancellationToken ct = default);
}