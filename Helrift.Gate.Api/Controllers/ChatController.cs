using Helrift.Gate.App;
using Helrift.Gate.Contracts;
using Helrift.Gate.Infrastructure;
using Helrift.Gate.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;

namespace Helrift.Gate.Api.Controllers;

[ApiController]
[Route("api/v1/chat")]
public sealed class ChatController(IChatBroadcaster broadcaster, IPresenceService presence, IGameServerConnectionRegistry gsRegistry, ILogger<WebSocketChatBroadcaster> logger) : ControllerBase
{
    // POST /api/v1/chat/broadcast
    [HttpPost("broadcast")]
    public async Task<IActionResult> Broadcast([FromBody] ChatBroadcastData dto, CancellationToken ct)
    {
        if (dto is null)
            return BadRequest("Body required.");

        if (string.IsNullOrWhiteSpace(dto.Message))
            return BadRequest("Message required.");

        logger.LogInformation($"[Chat] Broadcast requested {dto.Message} [{dto.ChatType.ToString()}]");

        await broadcaster.BroadcastAsync(dto, ct);
        return Accepted();
    }

    [HttpPost("whisper")]
    public async Task<IActionResult> Whisper([FromBody] WhisperSendDto dto, CancellationToken ct)
    {
        if (dto is null) return BadRequest("Body required.");
        if (string.IsNullOrWhiteSpace(dto.SenderGameServerId)) return BadRequest("SenderGameServerId required.");
        if (string.IsNullOrWhiteSpace(dto.SenderName)) return BadRequest("SenderName required.");
        if (string.IsNullOrWhiteSpace(dto.TargetName)) return BadRequest("TargetName required.");

        // 1) check presence
        var target = presence.GetByName(dto.TargetName);
        if (target == null)
        {
            return Ok(new WhisperSendResultDto
            {
                Delivered = false,
                Reason = "Player is not online"
            });
        }

        // 2) get target GS socket
        var targetSocket = gsRegistry.Get(target.GameServerId);
        if (targetSocket == null || targetSocket.State != WebSocketState.Open)
        {
            return Ok(new WhisperSendResultDto
            {
                Delivered = false,
                Reason = "Target game server not connected"
            });
        }

        // 3) build envelope to send to target GS
        var envelope = new
        {
            type = "chat.whisper.deliver",
            payload = new
            {
                SenderName = dto.SenderName,
                TargetName = dto.TargetName,
                Message = dto.Message,
                SenderSide = dto.SenderSide
            }
        };

        var json = JsonConvert.SerializeObject(envelope);
        var bytes = Encoding.UTF8.GetBytes(json);
        await targetSocket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);

        // 4) done
        return Ok(new WhisperSendResultDto
        {
            Delivered = true
        });
    }
}
