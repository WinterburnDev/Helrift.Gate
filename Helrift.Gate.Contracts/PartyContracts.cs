
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
    public PartyVisibility Visibility { get; set; } = PartyVisibility.Public;
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

