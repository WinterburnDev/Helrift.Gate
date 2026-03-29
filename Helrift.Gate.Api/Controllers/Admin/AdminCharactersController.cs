using Helrift.Gate.Api.Services.Accounts;
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;
using Helrift.Gate.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Helrift.Gate.Api.Controllers.Admin;

[ApiController]
[Route("admin/api/[controller]")]
public class CharactersController : ControllerBase
{
    private readonly ICharacterSearchService _search;
    private readonly IGameDataProvider _data;
    private readonly IPresenceService _presence;
    private readonly IGameServerConnectionRegistry _registry;
    private readonly IBanService _banService;

    public CharactersController(
        ICharacterSearchService search,
        IGameDataProvider data,
        IPresenceService presence,
        IGameServerConnectionRegistry registry,
        IBanService banService)
    {
        _search = search;
        _data = data;
        _presence = presence;
        _registry = registry;
        _banService = banService;
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Query parameter 'name' is required.");

        var results = await _search.SearchByNameAsync(name, ct);
        return Ok(results);
    }

    [HttpGet("{accountId}/{charId}")]
    public async Task<IActionResult> Get([FromRoute] string accountId, [FromRoute] string charId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(charId))
            return BadRequest("accountId and charId are required.");

        var character = await _data.GetCharacterAsync(accountId, charId, ct);
        if (character is null)
            return NotFound();

        return Ok(character);
    }

    [HttpPost("{accountId}/{charId}/kick")]
    public async Task<IActionResult> Kick(
        [FromRoute] string accountId,
        [FromRoute] string charId,
        [FromBody] AdminActionDto dto,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(charId))
            return BadRequest("accountId and charId are required.");

        var player = _presence.GetAll()
            .FirstOrDefault(p => string.Equals(p.CharacterId, charId, StringComparison.OrdinalIgnoreCase));

        if (player is null)
            return NotFound("Character is not currently online.");

        var ws = _registry.Get(player.GameServerId);
        if (ws is null || ws.State != WebSocketState.Open)
            return StatusCode(503, "Game server for this player is not connected.");

        var envelope = JsonSerializer.Serialize(new
        {
            type = "admin.kick",
            payload = new { characterId = charId, reason = dto?.Reason ?? "Kicked by admin" }
        });

        var bytes = Encoding.UTF8.GetBytes(envelope);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);

        return Ok(new { message = $"Kick sent to game server '{player.GameServerId}' for character '{charId}'." });
    }

    [HttpPost("{accountId}/{charId}/ban")]
    public async Task<IActionResult> Ban(
        [FromRoute] string accountId,
        [FromRoute] string charId,
        [FromBody] AdminBanCharacterDto dto,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(charId))
            return BadRequest("accountId and charId are required.");

        if (string.IsNullOrWhiteSpace(dto?.SteamId) && string.IsNullOrWhiteSpace(dto?.IpAddress))
            return BadRequest("Either SteamId or IpAddress must be provided.");

        if (string.IsNullOrWhiteSpace(dto.Reason))
            return BadRequest("Reason is required.");

        // Create the ban record
        var req = new CreateBanRequest
        {
            RealmId = dto.RealmId ?? "default",
            SteamId = dto.SteamId,
            IpAddress = dto.IpAddress,
            Reason = dto.Reason,
            DurationMinutes = dto.DurationMinutes,
            CreatedBy = dto.CreatedBy ?? "admin"
        };

        var record = await _banService.CreateBanAsync(req, ct);

        // Also kick if the player is currently online
        var player = _presence.GetAll()
            .FirstOrDefault(p => string.Equals(p.CharacterId, charId, StringComparison.OrdinalIgnoreCase));

        if (player is not null)
        {
            var ws = _registry.Get(player.GameServerId);
            if (ws is not null && ws.State == WebSocketState.Open)
            {
                var envelope = JsonSerializer.Serialize(new
                {
                    type = "admin.kick",
                    payload = new { characterId = charId, reason = $"Banned: {dto.Reason}" }
                });

                var bytes = Encoding.UTF8.GetBytes(envelope);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
            }
        }

        return Ok(record);
    }
}

public sealed class AdminActionDto
{
    public string? Reason { get; set; }
    public string? InitiatedBy { get; set; }
}

public sealed class AdminBanCharacterDto
{
    public string RealmId { get; set; } = "default";
    public string? SteamId { get; set; }
    public string? IpAddress { get; set; }
    public string Reason { get; set; } = "";
    public int? DurationMinutes { get; set; }
    public string? CreatedBy { get; set; }
}