
using Helrift.Gate.Contracts;

public sealed class PartyDto
{
    public string PartyId { get; set; } = default!;
    public string LeaderCharacterId { get; set; } = default!;
    public string Side { get; set; }
    public string PartyName { get; set; }
    public PartyExpMode ExpMode { get; set; }
    public string Visibility { get; set; } = "Public";
    public List<PartyMemberDto> Members { get; set; } = new();
}

public class PartyMemberDto
{
    public string CharacterId { get; set; } = default!;
    public string CharacterName { get; set; } = default!;
    public bool IsOnline { get; set; }
    public string? CurrentServerId { get; set; }
}

public class CreatePartyRequest
{
    public string AccountId { get; set; } = default!;
    public string CharacterId { get; set; } = default!;
    public string CharacterName { get; set; } = default!;
    public string PartyName { get; set; } = default!;
    public string Side { get; set; } = default!;
    public string Visibility { get; set; } = default!;
}

public class JoinPartyRequest
{
    public string PartyId { get; set; } = default!;
    public string AccountId { get; set; } = default!;
    public string CharacterId { get; set; } = default!;
    public string CharacterName { get; set; } = default!;
}

public class LeavePartyRequest
{
    public string CharacterId { get; set; } = default!;
}

public class SetLeaderRequest
{
    public string PartyId { get; set; } = default!;
    public string NewLeaderCharacterId { get; set; } = default!;
}

public class KickMemberRequest
{
    public string PartyId { get; set; } = default!;
    public string KickerCharacterId { get; set; } = default!;
    public string TargetCharacterId { get; set; } = default!;
}

public sealed class PartyExperienceEventBatchDto
{
    public string GameServerId { get; set; } = default!;
    public List<PartyExperienceEventDto> Events { get; set; } = new();
}

public sealed class PartyExperienceEventDto
{
    public string EventId { get; set; } = default!;
    public string PartyId { get; set; } = default!;
    public string EarnerCharacterId { get; set; } = default!;
    public int BaseXp { get; set; }

    public float SourceX { get; set; }
    public float SourceY { get; set; }
    public float SourceZ { get; set; }

    public long OccurredAtUnixMs { get; set; }
}

/// <summary>
/// Payload sent over WS to a specific GS. Mirrors party.state pattern with recipients.
/// </summary>
public sealed class PartyExperiencePayloadDto
{
    public string PartyId { get; set; } = default!;
    public float SourceX { get; set; }
    public float SourceY { get; set; }
    public float SourceZ { get; set; }

    public float ShareRange { get; set; }

    public string[] Recipients { get; set; } = Array.Empty<string>();
    public List<PartyExperienceDeltaDto> Deltas { get; set; } = new();
}

public sealed class PartyExperienceDeltaDto
{
    public string EventId { get; set; } = default!;
    public string CharacterId { get; set; } = default!;
    public int BaseXpShare { get; set; }
}