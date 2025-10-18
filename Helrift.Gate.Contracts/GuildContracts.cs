// Helrift.Gate.Contracts/GuildContracts.cs
using System;
using System.Collections.Generic;

namespace Helrift.Gate.Contracts;

public sealed class GuildData
{
    public string GuildId { get; set; } = "";
    public string Name { get; set; } = "";
    public string LeaderCharacterId { get; set; } = "";
    public DateTime? CreatedAt { get; set; } // ISO when present
    public string Side { get; set; } = "";   // matches Unity enum name strings
    public List<string> MemberCharacterIds { get; set; } = new();
    public string? MOTD { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, object>? Emblem { get; set; } // pass-through
}
