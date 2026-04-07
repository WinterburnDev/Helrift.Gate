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

    /// <summary>
    /// Requirement pools keyed by pool ID. Each pool holds the rotation-variant requirement
    /// entries for one or more project definitions.
    /// </summary>
    public Dictionary<string, TownProjectRequirementPool> RequirementPools { get; init; } = new();

    /// <summary>
    /// Curated item groups keyed by group ID. Used by requirement entries to express
    /// "any item from this group" without relying on broad item categories.
    /// </summary>
    public Dictionary<string, TownProjectItemGroup> ItemGroups { get; init; } = new();
}

/// <summary>
/// Individual project definition. Defines identity, narrative, reward, and timing.
/// Contribution details (what to deliver/kill, how much) live in the RequirementPool
/// referenced by RequirementPoolId. Definitions without a pool use the legacy flat
/// fields (ContributionType, RequiredItemId, TargetProgress, etc.) for backward compatibility.
/// </summary>
public sealed record TownProjectDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public TownProjectCategory Category { get; init; }

    // ---- Requirement pool (new) ----

    /// <summary>
    /// ID of the requirement pool to use when rolling a new instance.
    /// When set, pool entries drive the contribution type, quantities, and item constraints.
    /// When null, the legacy flat fields below are used.
    /// </summary>
    public string? RequirementPoolId { get; init; }

    // ---- Legacy flat requirement fields (backward compat) ----
    // These are used when RequirementPoolId is null. Prefer RequirementPoolId for new projects.

    public TownProjectContributionType ContributionType { get; init; }
    public string? RequiredItemId { get; init; }
    public int TargetProgress { get; init; }
    public int ProgressPerContributionUnit { get; init; }
    public int ReputationPerContributionUnit { get; init; }

    // ---- Reward ----

    public TownProjectRewardType RewardType { get; init; }
    public TownProjectRewardScope RewardScope { get; init; }
    public string RewardValue { get; init; } = string.Empty;
    public int RewardDurationSeconds { get; init; }
    public TownProjectEventType EventType { get; init; }
    public bool IsEnabled { get; init; } = true;
    public TownProjectIndividualRewardMode IndividualRewardMode { get; init; } = TownProjectIndividualRewardMode.AllCitizens;
}

// ============================================================
//  Requirement Pools
// ============================================================

/// <summary>
/// Selection mode for choosing a requirement entry from a pool.
/// </summary>
public enum RequirementPoolSelectionMode
{
    /// <summary>Weighted random pick. Each entry's Weight controls relative probability.</summary>
    WeightedRandom = 0,

    /// <summary>Rotate through entries in declaration order, cycling back to the start.</summary>
    Sequential = 1
}

/// <summary>
/// A pool of requirement entries for a project definition. Gate selects one entry when
/// a new project instance is created, applying anti-repetition rules to avoid picking
/// the same variant back-to-back.
/// </summary>
public sealed record TownProjectRequirementPool
{
    public string Id { get; init; } = string.Empty;

    /// <summary>How to pick an entry. Default is WeightedRandom.</summary>
    public RequirementPoolSelectionMode SelectionMode { get; init; } = RequirementPoolSelectionMode.WeightedRandom;

    /// <summary>
    /// When true, the last-used entry is excluded from consideration provided
    /// at least one other valid entry exists.
    /// </summary>
    public bool PreventImmediateRepeat { get; init; } = true;

    /// <summary>All available requirement variants for this pool.</summary>
    public List<TownProjectRequirementEntry> Entries { get; init; } = new();
}

/// <summary>
/// One concrete requirement variant inside a requirement pool. Describes what players
/// must deliver or kill, in what quantity, subject to any quality/condition constraints.
/// </summary>
public sealed record TownProjectRequirementEntry
{
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Relative selection weight for WeightedRandom pools. Higher = more likely. Default 1.
    /// </summary>
    public int Weight { get; init; } = 1;

    public TownProjectContributionType ContributionType { get; init; }

    /// <summary>Total units required to complete the project when this entry is active.</summary>
    public int TargetQuantity { get; init; }

    /// <summary>Progress points credited per contribution unit.</summary>
    public int ProgressPerUnit { get; init; }

    /// <summary>Reputation points awarded per contribution unit.</summary>
    public int ReputationPerUnit { get; init; }

    // ---- Item delivery specifics (only relevant when ContributionType == ItemDelivery) ----

    /// <summary>
    /// One or more explicit item IDs that satisfy this requirement.
    /// Mutually exclusive with AllowedItemGroupId; set only one.
    /// </summary>
    public List<string>? AllowedItemIds { get; init; }

    /// <summary>
    /// ID of a <see cref="TownProjectItemGroup"/> whose items all satisfy this requirement.
    /// Mutually exclusive with AllowedItemIds; set only one.
    /// </summary>
    public string? AllowedItemGroupId { get; init; }

    /// <summary>Optional quality constraint applied to all item checks for this entry.</summary>
    public ItemQualityRule? QualityRule { get; init; }

    /// <summary>Optional condition/durability constraint applied to all item checks for this entry.</summary>
    public ItemConditionRule? ConditionRule { get; init; }
}

// ============================================================
//  Item Groups
// ============================================================

/// <summary>
/// A curated, config-defined collection of item IDs used as a requirement group.
/// These are NOT broad master item categories; they are designer-controlled sets
/// specific to Town Projects. The same item may appear in multiple groups.
/// </summary>
public sealed record TownProjectItemGroup
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary>All item IDs that belong to this group.</summary>
    public List<string> ItemIds { get; init; } = new();
}

// ============================================================
//  Quality and Condition Rules
// ============================================================

/// <summary>How an item quality constraint is evaluated.</summary>
public enum ItemQualityRuleMode
{
    /// <summary>No quality restriction; any quality is accepted.</summary>
    None = 0,

    /// <summary>Only items whose quality exactly matches <see cref="ItemQualityRule.QualityValue"/> are accepted.</summary>
    Exact = 1,

    /// <summary>Items with quality >= <see cref="ItemQualityRule.QualityValue"/> are accepted.</summary>
    Minimum = 2
}

/// <summary>
/// Describes a quality constraint for an item delivery requirement.
/// </summary>
public sealed record ItemQualityRule
{
    public ItemQualityRuleMode Mode { get; init; }

    /// <summary>
    /// The quality tier string to match or use as a floor (e.g. "Sturdy", "Pristine").
    /// Required when Mode is Exact or Minimum; ignored when Mode is None.
    /// </summary>
    public string? QualityValue { get; init; }
}

/// <summary>How an item condition/durability constraint is evaluated.</summary>
public enum ItemConditionRuleMode
{
    /// <summary>No condition restriction; any condition is accepted.</summary>
    Any = 0,

    /// <summary>Only items whose condition exactly equals <see cref="ItemConditionRule.Threshold"/> are accepted.</summary>
    Exact = 1,

    /// <summary>Only items whose condition is >= <see cref="ItemConditionRule.Threshold"/> are accepted.</summary>
    Minimum = 2
}

/// <summary>
/// Describes a condition/durability constraint for an item delivery requirement.
/// Condition is expressed as a normalised float in the range [0.0, 1.0] where
/// 1.0 represents pristine/full condition.
///
/// TODO: If the item model in the Unity project uses a different representation
/// (e.g. integer durability points), update Threshold accordingly and document
/// the mapping in both Gate and Unity.
/// </summary>
public sealed record ItemConditionRule
{
    public ItemConditionRuleMode Mode { get; init; }

    /// <summary>
    /// Condition threshold in [0.0, 1.0]. Required when Mode is Exact or Minimum;
    /// ignored when Mode is Any.
    /// </summary>
    public float? Threshold { get; init; }
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

    // ---- Rolled requirement (set at instance creation, never re-derived) ----

    /// <summary>
    /// ID of the requirement entry that was rolled from the pool when this instance was created.
    /// Null for instances created from legacy pool-less definitions.
    /// </summary>
    public string? RequirementEntryId { get; set; }

    /// <summary>
    /// Full snapshot of the rolled requirement entry persisted at instance creation time.
    /// Gate and Unity must treat this as the authoritative requirement for this instance's
    /// entire lifetime. Never re-derive from the pool; the pool may change between rotations.
    /// </summary>
    public TownProjectRequirementEntry? ResolvedRequirement { get; set; }
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