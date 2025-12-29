// Helrift.Gate.Contracts/ChatBroadcastData.cs
using System;

namespace Helrift.Gate.Contracts;

[Serializable]
public sealed class ChatBroadcastData
{
    public int SenderId { get; set; }
    public string ChatType { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public string PartyId { get; set; } = string.Empty;
    public string GuildId { get; set; } = string.Empty;
    public string OriginServerId { get; set; } = string.Empty;
    public bool IsAdmin { get; set; } = false;
}

public sealed class WhisperSendDto
{
    public string SenderGameServerId { get; set; }
    public string SenderName { get; set; }
    public string? SenderSide { get; set; }
    public string TargetName { get; set; }
    public string Message { get; set; }
    public bool IsAdmin { get; set; }
}

public sealed class WhisperSendResultDto
{
    public bool Delivered { get; set; }
    public string? Reason { get; set; }
}