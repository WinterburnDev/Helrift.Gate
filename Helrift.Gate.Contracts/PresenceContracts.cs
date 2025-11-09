public sealed class OnlinePlayer
{
    public string CharacterId { get; set; }
    public string CharacterName { get; set; }
    public string GameServerId { get; set; }
    public string Side { get; set; }
    public DateTime LastSeenUtc { get; set; }
}

public sealed class PlayerOnlineDto
{
    public string CharacterId { get; set; }
    public string CharacterName { get; set; }
    public string Side { get; set; }
}

public sealed class PresenceRegisterDto
{
    public string GameServerId { get; set; }
    public string CharacterId { get; set; }
    public string CharacterName { get; set; }
    public string Side { get; set; }
}

public sealed class PresenceUnregisterDto
{
    public string GameServerId { get; set; }
    public string CharacterId { get; set; }
    public string CharacterName { get; set; }
}

public sealed class PresenceFullSyncDto
{
    public string GameServerId { get; set; }
    public List<PlayerOnlineDto> Players { get; set; }
}