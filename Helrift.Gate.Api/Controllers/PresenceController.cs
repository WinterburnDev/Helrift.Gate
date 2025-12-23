using Microsoft.AspNetCore.Mvc;

using Helrift.Gate.Contracts;
using Helrift.Gate.Services;

[ApiController]
[Route("api/v1/presence")]
public sealed class PresenceController(IPresenceService presence) : ControllerBase
{
    // GS calls this when a player logs in / character loads
    [HttpPost("register")]
    public IActionResult Register([FromBody] PresenceRegisterDto dto)
    {
        if (dto == null) return BadRequest();
        if (string.IsNullOrWhiteSpace(dto.GameServerId)) return BadRequest("GameServerId required");
        if (string.IsNullOrWhiteSpace(dto.CharacterName)) return BadRequest("CharacterName required");

        presence.RegisterGameServer(dto.GameServerId);
        presence.AddOrUpdatePlayer(dto.GameServerId, dto.CharacterId, dto.CharacterName, dto.Side);
        return Ok();
    }

    // GS calls this when a player logs out / disconnects
    [HttpPost("unregister")]
    public IActionResult Unregister([FromBody] PresenceUnregisterDto dto)
    {
        if (dto == null) return BadRequest();
        if (string.IsNullOrWhiteSpace(dto.GameServerId)) return BadRequest("GameServerId required");
        if (string.IsNullOrWhiteSpace(dto.CharacterName)) return BadRequest("CharacterName required");

        presence.RemovePlayer(dto.GameServerId, dto.CharacterId, dto.CharacterName);

        return Ok();
    }

    // GS calls this after *it* restarts or after *Gate* restarts to resync everyone
    [HttpPost("fullsync")]
    public IActionResult FullSync([FromBody] PresenceFullSyncDto dto)
    {
        if (dto == null) return BadRequest();
        if (string.IsNullOrWhiteSpace(dto.GameServerId)) return BadRequest("GameServerId required");

        presence.RegisterGameServer(dto.GameServerId);
        presence.ReplacePlayersForServer(dto.GameServerId, dto.Players ?? []);
        return Ok();
    }

    [HttpGet("online")]
    public IActionResult GetOnline()
    {
        var players = presence.GetAll();
        return Ok(players);
    }

    // optional: GET /api/v1/presence/online/{name}
    [HttpGet("online/{name}")]
    public IActionResult GetOnlineByName(string name)
    {
        var player = presence.GetByName(name);
        return player is null ? NotFound() : Ok(player);
    }
}