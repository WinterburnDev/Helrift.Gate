using Helrift.Gate.App.Repositories;

namespace Helrift.Gate.Api.Services.ConfigPlatform;

public enum ConfigValidationSeverity
{
    Error = 0,
    Warning = 1
}

public sealed record ConfigValidationIssue
{
    public ConfigValidationSeverity Severity { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed record ConfigValidationResult
{
    public List<ConfigValidationIssue> Issues { get; init; } = new();
    public bool IsValid => !Issues.Any(i => i.Severity == ConfigValidationSeverity.Error);
}

public sealed record ConfigVersionSummary
{
    public string Version { get; init; } = string.Empty;
    public DateTime UpdatedAt { get; init; }
    public string UpdatedBy { get; init; } = string.Empty;
    public int DefinitionCount { get; init; }
}

public sealed record RealmVersionSelection
{
    public string RealmId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
}

public sealed record ConfigCompareResult
{
    public string LeftVersion { get; init; } = string.Empty;
    public string RightVersion { get; init; } = string.Empty;
    public IReadOnlyList<string> AddedDefinitionIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RemovedDefinitionIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ChangedDefinitionIds { get; init; } = Array.Empty<string>();
}

public sealed record ConfigSaveResult
{
    public bool Saved { get; init; }
    public string Version { get; init; } = string.Empty;
    public ConfigValidationResult Validation { get; init; } = new();
}

/// <summary>
/// Generic base service for versioned configuration domains.
/// </summary>
public abstract class ConfigDomainServiceBase<TConfig, TDefinition, TRealmRef>
{
    private readonly IVersionedConfigRepository<TConfig, TRealmRef> _repository;

    protected ConfigDomainServiceBase(IVersionedConfigRepository<TConfig, TRealmRef> repository)
    {
        _repository = repository;
    }

    protected abstract string? GetRealmVersion(TRealmRef? realmRef);
    protected abstract TRealmRef CreateRealmRef(string version);
    protected abstract string GetVersion(TConfig config);
    protected abstract DateTime GetUpdatedAt(TConfig config);
    protected abstract string GetUpdatedBy(TConfig config);
    protected abstract IReadOnlyDictionary<string, TDefinition> GetDefinitions(TConfig config);
    protected abstract bool DefinitionEquals(TDefinition left, TDefinition right);
    protected abstract ConfigValidationResult Validate(TConfig config);

    public virtual async Task<IReadOnlyList<ConfigVersionSummary>> ListVersionsAsync(CancellationToken ct = default)
    {
        var versions = await _repository.ListConfigVersionsAsync(ct);
        return versions
            .Select(v => new ConfigVersionSummary
            {
                Version = GetVersion(v.Config),
                UpdatedAt = GetUpdatedAt(v.Config),
                UpdatedBy = GetUpdatedBy(v.Config),
                DefinitionCount = GetDefinitions(v.Config).Count
            })
            .OrderByDescending(v => v.UpdatedAt)
            .ThenByDescending(v => v.Version)
            .ToList();
    }

    public virtual Task<TConfig?> GetVersionAsync(string version, CancellationToken ct = default)
        => _repository.GetConfigVersionAsync(version, ct);

    public virtual async Task<IReadOnlyList<RealmVersionSelection>> ListRealmSelectionsAsync(CancellationToken ct = default)
    {
        var refs = await _repository.ListRealmConfigRefsAsync(ct);
        return refs
            .Select(r => new RealmVersionSelection
            {
                RealmId = r.RealmId,
                Version = GetRealmVersion(r.ConfigRef) ?? string.Empty
            })
            .Where(r => !string.IsNullOrWhiteSpace(r.Version))
            .OrderBy(r => r.RealmId)
            .ToList();
    }

    public virtual async Task<string?> GetRealmSelectionAsync(string realmId, CancellationToken ct = default)
    {
        var realmRef = await _repository.GetRealmConfigRefAsync(realmId, ct);
        return GetRealmVersion(realmRef);
    }

    public virtual async Task SetRealmSelectionAsync(string realmId, string version, CancellationToken ct = default)
    {
        var target = await _repository.GetConfigVersionAsync(version, ct);
        if (target == null)
            throw new InvalidOperationException($"Config version '{version}' not found.");

        await _repository.SaveRealmConfigRefAsync(realmId, CreateRealmRef(version), ct);
    }

    public virtual Task<ConfigValidationResult> ValidateAsync(TConfig config, CancellationToken ct = default)
        => Task.FromResult(Validate(config));

    public virtual async Task<ConfigSaveResult> SaveVersionAsync(TConfig config, CancellationToken ct = default)
    {
        var validation = Validate(config);
        if (!validation.IsValid)
        {
            return new ConfigSaveResult
            {
                Saved = false,
                Version = GetVersion(config),
                Validation = validation
            };
        }

        await _repository.SaveConfigVersionAsync(GetVersion(config), config, ct);

        return new ConfigSaveResult
        {
            Saved = true,
            Version = GetVersion(config),
            Validation = validation
        };
    }

    public virtual async Task<ConfigCompareResult> CompareVersionsAsync(string leftVersion, string rightVersion, CancellationToken ct = default)
    {
        var left = await _repository.GetConfigVersionAsync(leftVersion, ct)
            ?? throw new InvalidOperationException($"Config version '{leftVersion}' not found.");
        var right = await _repository.GetConfigVersionAsync(rightVersion, ct)
            ?? throw new InvalidOperationException($"Config version '{rightVersion}' not found.");

        var leftDefs = GetDefinitions(left);
        var rightDefs = GetDefinitions(right);

        var added = rightDefs.Keys.Except(leftDefs.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var removed = leftDefs.Keys.Except(rightDefs.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

        var common = leftDefs.Keys.Intersect(rightDefs.Keys, StringComparer.OrdinalIgnoreCase);
        var changed = common
            .Where(id => !DefinitionEquals(leftDefs[id], rightDefs[id]))
            .OrderBy(x => x)
            .ToList();

        return new ConfigCompareResult
        {
            LeftVersion = leftVersion,
            RightVersion = rightVersion,
            AddedDefinitionIds = added,
            RemovedDefinitionIds = removed,
            ChangedDefinitionIds = changed
        };
    }
}