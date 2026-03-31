using System;
using System.Collections.Generic;

namespace Helrift.Gate.Contracts;

public enum EscrowSupportStatus : byte
{
    Supported = 1,
    Partial = 2,
    NotSupported = 3
}

public sealed class EscrowCapability
{
    public EscrowAssetType AssetType { get; set; }
    public string Key { get; set; } = string.Empty;
    public EscrowSupportStatus Status { get; set; }
    public string Note { get; set; } = string.Empty;
}

public sealed class EscrowIntegrityIssue
{
    public string Severity { get; set; } = "warning";
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? AssetId { get; set; }
    public string? OperationIdempotencyKey { get; set; }
}

public sealed class EscrowIntegrityReport
{
    public string RealmId { get; set; } = "default";
    public string ContainerId { get; set; } = string.Empty;
    public DateTime GeneratedUtc { get; set; }
    public bool Healthy { get; set; } = true;
    public List<EscrowIntegrityIssue> Issues { get; set; } = [];
}