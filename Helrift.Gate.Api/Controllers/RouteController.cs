using Helrift.Gate.Api.Services.GameServers;
using Helrift.Gate.Api.Services.Routing;
using Helrift.Gate.Api.Services.Tokens;
using Helrift.Gate.App;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/v1/route")]
public sealed class RouteController(
    IGameDataProvider data,
    IGameServerDirectory directory,
    IJoinTokenService tokenSvc,
    IReservationClient reserveClient,
    Microsoft.Extensions.Options.IOptions<JwtJoinOptions> joinOpt) : ControllerBase
{
    private readonly JwtJoinOptions _joinOpt = joinOpt.Value;

    // POST /api/v1/route/character
    [HttpPost("character")]
    [Authorize]
    public async Task<ActionResult<CharacterRouteResponse>> RouteCharacter([FromBody] CharacterRouteRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.characterId))
            return BadRequest("characterId required.");

        var accountId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var build = User.FindFirst("build")?.Value; // if you include build in your access jwt; optional

        // 1) Validate character ownership and get current map
        var character = await data.GetCharacterAsync(accountId, req.characterId, ct);
        if (character is null)
            return NotFound("Character not found or not owned by caller.");

        var mapId = character.MapId; // adapt to your model

        // 2) Pick the right game server
        var gs = directory.PickForCharacter(mapId);
        if (gs is null) return StatusCode(503, "No game servers available.");

        // 3) Mint join token bound to account/char/build/gs
        var joinToken = tokenSvc.MintJoinToken(
            accountId,
            character.Id,
            gs.Id,
            build ?? "" ?? "unknown",
            out var jti);

        // 4) Ask GS to reserve the join (TODO)
        //var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_joinOpt.JoinMinutes);
        //var ok = await reserveClient.ReserveAsync(gs, accountId, character.Id, jti, expiresAt, ct);
        //if (!ok) return StatusCode(503, "Game server could not reserve a slot.");

        // 5) Return route info to client
        return Ok(new CharacterRouteResponse
        {
            gsId = gs.Id,
            ip = gs.PublicIp,
            port = gs.GamePort,
            joinToken = joinToken,
            transport = new TransportHints { type = "fishnet.tugboat" }
        });
    }
}