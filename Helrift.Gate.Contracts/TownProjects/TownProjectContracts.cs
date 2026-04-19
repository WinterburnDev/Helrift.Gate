// Contracts/TownProjects/TownProjectContracts.cs
namespace Helrift.Gate.Contracts.TownProjects;

/// <summary>
/// Root configuration container for Town Projects loaded from Firebase.
/// Path: /config/projects/versions/{version}
/// </summary>
public sealed record TownProjectConfigRoot
{
    public string Version { get; init; } = string.Empty;
    public DateTime UpdatedAt { get; init; }
    public string UpdatedBy { get; init; } = string.Empty;
    public DateTime? PublishedAt { get; init; }
    public string? PublishedBy { get; init; }
    public Dictionary<string, TownProjectDefinition> Definitions { get; init; } = new();
    public Dictionary<string, TownProjectRequirementPool> RequirementPools { get; init; } = new();
    public Dictionary<string, TownProjectItemGroup> ItemGroups { get; init; } = new();
}

/// <summary>
/// Individual project definition.
/// </summary>
public sealed record TownProjectDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public TownProjectCategory Category { get; init; }
    public string RequirementPoolId { get; init; } = string.Empty;

    // Legacy fallback fields for older configs.
    public TownProjectContributionType ContributionType { get; init; }
    public string? RequiredItemId { get; init; }
    public int TargetProgress { get; init; }
    public int ProgressPerContributionUnit { get; init; }
    public int ReputationPerContributionUnit { get; init; }

    public TownProjectRewardType RewardType { get; init; }
    public TownProjectRewardScope RewardScope { get; init; }
    public string RewardValue { get; init; } = string.Empty;
    public int RewardDurationSeconds { get; init; }
    public TownProjectEventType EventType { get; init; }
    public bool IsEnabled { get; init; } = true;
    public TownProjectIndividualRewardMode IndividualRewardMode { get; init; } = TownProjectIndividualRewardMode.AllCitizens;
}

public sealed record TownProjectRequirementPool
{
    public string Id { get; init; } = string.Empty;
    public TownProjectRequirementSelectionMode SelectionMode { get; init; } = TownProjectRequirementSelectionMode.WeightedRandom;
    public bool PreventImmediateRepeat { get; init; } = true;
    public int RecentHistorySize { get; init; } = 1;
    public List<TownProjectRequirementEntry> Entries { get; init; } = new();
}

public sealed record TownProjectRequirementEntry
{
    public string Id { get; init; } = string.Empty;
    public int Weight { get; init; } = 1;
    public TownProjectContributionType ContributionType { get; init; }
    public int TargetQuantity { get; init; }
    public int ProgressPerUnit { get; init; }
    public int ReputationPerUnit { get; init; }
    public List<string> AllowedItemIds { get; init; } = new();
    public string? AllowedItemGroupId { get; init; }
    public ItemQualityRule? QualityRule { get; init; }
    public ItemConditionRule? ConditionRule { get; init; }
}

public sealed record TownProjectItemGroup
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public List<string> ItemIds { get; init; } = new();
}

public sealed record ItemQualityRule
{
    public ItemQualityRuleMode Mode { get; init; } = ItemQualityRuleMode.None;
    public TownProjectItemQuality Quality { get; init; } = TownProjectItemQuality.Unknown;
}

public sealed record ItemConditionRule
{
    public ItemConditionRuleMode Mode { get; init; } = ItemConditionRuleMode.Any;
    public int ExactEndurance { get; init; }
    public int MinimumEndurance { get; init; }
    public int MinimumEndurancePercent { get; init; }
}

/// <summary>
/// Realm configuration reference that points to a specific projects version.
/// Path: /realms/{realmId}/config
/// </summary>
public sealed record RealmProjectConfigRef
{
    public string ProjectsVersion { get; init; } = string.Empty;
}

/// <summary>
/// Active project instance for a specific town.
/// Path: /realms/{realmId}/townProjects/instances/{townId}/{projectInstanceId}
/// </summary>
public sealed class TownProjectInstance
{
    public string Id { get; set; } = string.Empty;
    public string DefinitionId { get; set; } = string.Empty;
    public string RequirementEntryId { get; set; } = string.Empty;
    public TownProjectResolvedRequirement? ResolvedRequirement { get; set; }
    public string TownId { get; set; } = string.Empty;
    public string RealmId { get; set; } = string.Empty;
    public TownProjectStatus Status { get; set; }
    public int CurrentProgress { get; set; }
    public int TargetProgress { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string? EventInstanceId { get; set; }
    public List<TownProjectContribution> Contributions { get; set; } = new();
    public Dictionary<string, int> ContributorReputation { get; set; } = new();
    public int Version { get; set; }
}

public sealed class TownProjectResolvedRequirement
{
    public string EntryId { get; set; } = string.Empty;
    public TownProjectContributionType ContributionType { get; set; }
    public int TargetQuantity { get; set; }
    public int ProgressPerUnit { get; set; }
    public int ReputationPerUnit { get; set; }
    public List<string> AllowedItemIds { get; set; } = new();
    public string? AllowedItemGroupId { get; set; }
    public ItemQualityRule? QualityRule { get; set; }
    public ItemConditionRule? ConditionRule { get; set; }
}

/// <summary>
/// Individual contribution to a project.
/// </summary>
public sealed class TownProjectContribution
{
    public string ContributorCharacterId { get; set; } = string.Empty;
    public string ContributorAccountId { get; set; } = string.Empty;
    public int ProgressContributed { get; set; }
    public int ReputationEarned { get; set; }
    public DateTime ContributedAtUtc { get; set; }
}

/// <summary>
/// Active reward state for a town.
/// Path: /realms/{realmId}/townProjects/rewards/{townId}/{rewardId}
/// </summary>
public sealed class TownProjectRewardState
{
    public string Id { get; set; } = string.Empty;
    public string TownId { get; set; } = string.Empty;
    public string RealmId { get; set; } = string.Empty;
    public string ProjectDefinitionId { get; set; } = string.Empty;
    public string ProjectInstanceId { get; set; } = string.Empty;
    public TownProjectRewardType RewardType { get; set; }
    public TownProjectRewardScope RewardScope { get; set; }
    public string RewardValue { get; set; } = string.Empty;
    public DateTime ActivatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string? EventInstanceId { get; set; }
    public bool IsActive { get; set; }
}

public sealed class TownProjectRequirementSelectionHistory
{
    public string RealmId { get; set; } = string.Empty;
    public string TownId { get; set; } = string.Empty;
    public string DefinitionId { get; set; } = string.Empty;
    public List<string> RecentRequirementEntryIds { get; set; } = new();
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// Town project status.
/// </summary>
public enum TownProjectStatus
{
    Unknown = 0,
    Active = 1,
    CompletedPendingActivation = 2,
    CompletedActivated = 3,
    Failed = 4,
    Expired = 5
}

/// <summary>
/// Category of town project.
/// </summary>
public enum TownProjectCategory
{
    Unknown = 0,
    WeeklyGeneral = 1,
    CrusadePreparation = 2
}

/// <summary>
/// Type of contribution required.
/// </summary>
public enum TownProjectContributionType
{
    Unknown = 0,
    MonsterKill = 1,
    PlayerKill = 2,
    ItemDelivery = 3
}

public enum TownProjectRequirementSelectionMode
{
    Unknown = 0,
    WeightedRandom = 1
}

public enum ItemQualityRuleMode
{
    None = 0,
    Exact = 1,
    Minimum = 2
}

public enum TownProjectItemQuality
{
    Unknown = 0,
    Flimsy = 1,
    Sturdy = 2,
    Reinforced = 3,
    Studded = 4
}

public enum ItemConditionRuleMode
{
    Any = 0,
    PristineOnly = 1,
    ExactEndurance = 2,
    MinimumEndurance = 3,
    MinimumEndurancePercent = 4
}

/// <summary>
/// Type of reward granted.
/// </summary>
public enum TownProjectRewardType
{
    Unknown = 0,
    Buff = 1,
    Currency = 2,
    Item = 3
}

/// <summary>
/// Scope of reward distribution.
/// </summary>
public enum TownProjectRewardScope
{
    Unknown = 0,
    Individual = 1,
    Town = 2,
    Realm = 3
}

/// <summary>
/// Event type that activates the project.
/// </summary>
public enum TownProjectEventType
{
    Unknown = 0,
    WeeklyReset = 1,
    CrusadeStart = 2,
    Manual = 3
}

/// <summary>
/// Mode for distributing individual rewards.
/// </summary>
public enum TownProjectIndividualRewardMode
{
    Unknown = 0,
    AllCitizens = 1,
    Contributors = 2,
    TopContributors = 3
}

/// <summary>
/// Metadata about loaded config.
/// </summary>
public sealed record TownProjectConfigMetadata
{
    public string RealmId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public DateTime UpdatedAt { get; init; }
    public string UpdatedBy { get; init; } = string.Empty;
    public DateTime? PublishedAt { get; init; }
    public string? PublishedBy { get; init; }
    public int DefinitionCount { get; init; }
    public DateTime LoadedAt { get; init; }
}