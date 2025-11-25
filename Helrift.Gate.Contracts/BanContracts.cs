namespace Helrift.Gate.Contracts;

public sealed class BanRecord
{
    public string Id { get; set; } = string.Empty;     // optional, firebase key if you need it
    public string RealmId { get; set; } = "default";   // if you want realm-specific bans

    public string SteamId { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;

    public long BannedAtUnixUtc { get; set; }
    public long? ExpiresAtUnixUtc { get; set; }        // null => permanent ban

    public string Reason { get; set; } = string.Empty; // "Toxic behaviour", "Cheating", etc.
    public string BannedBy { get; set; } = string.Empty;
}

public class BannedResponse
{
    public string message { get; set; }              // Human readable
    public long bannedAtUnixUtc { get; set; }
    public long? bannedUntilUnixUtc { get; set; }    // null for permanent
    public string reason { get; set; }               // short reason to show
}

public sealed class CreateBanRequest
{
    public string RealmId { get; set; } = "";

    // At least one of these must be provided
    public string? SteamId { get; set; }
    public string? IpAddress { get; set; }

    public string Reason { get; set; } = "";

    /// <summary>
    /// Null = permanent ban.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; set; }

    public string CreatedBy { get; set; } = "";

    public int? DurationMinutes { get; set; }

}
public sealed class CreateBanDto
{
    public string RealmId { get; set; } = "default";
    public string? SteamId { get; set; }
    public string? IpAddress { get; set; }
    public string Reason { get; set; } = "";
    public int? DurationMinutes { get; set; }  // null = permanent
    public string CreatedBy { get; set; } = "admin";
}