using Helrift.Gate.Contracts.Realm;
using Helrift.Gate.Infrastructure;
using Helrift.Gate.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.WebSockets;
using System.Text;

namespace Helrift.Gate.Api.Controllers;

[ApiController]
[Route("api/v1/realm")]
public sealed class RealmController(
    IRealmService realm,
    IGameServerConnectionRegistry gsRegistry,
    ILogger<RealmController> logger
) : ControllerBase
{
    // GET /api/v1/realm/state
    [HttpGet("state")]
    public IActionResult GetState()
    {
        var state = realm.GetState();
        return Ok(ToDto(state));
    }

    // POST /api/v1/realm/shutdown/schedule
    [HttpPost("shutdown/schedule")]
    public async Task<IActionResult> ScheduleShutdown([FromBody] ScheduleRealmShutdownRequest dto, CancellationToken ct)
    {
        if (dto is null) return BadRequest("Body required.");
        if (dto.Minutes <= 0) return BadRequest("Minutes must be > 0.");
        if (string.IsNullOrWhiteSpace(dto.Message)) return BadRequest("Message required.");
        if (string.IsNullOrWhiteSpace(dto.InitiatedBy)) return BadRequest("InitiatedBy required.");

        realm.ScheduleShutdown(TimeSpan.FromMinutes(dto.Minutes), dto.Message, dto.InitiatedBy);
        var state = realm.GetState();

        logger.LogInformation("[Realm] Shutdown scheduled in {Minutes} minutes by {By}. Deadline={DeadlineUtc}.",
            dto.Minutes, dto.InitiatedBy, state.ShutdownAtUtc);

        await FanoutRealmStateToGameServersAsync(ToDto(state), ct);

        return Accepted(ToDto(state));
    }

    // POST /api/v1/realm/clear
    [HttpPost("clear")]
    public async Task<IActionResult> Clear([FromBody] ClearRealmRequest? dto, CancellationToken ct)
    {
        // dto optional so operator can just POST with empty body
        var by = dto?.InitiatedBy ?? "unknown";

        realm.ClearAllOperations();
        var state = realm.GetState();

        logger.LogInformation("[Realm] Realm ops cleared by {By}.", by);

        await FanoutRealmStateToGameServersAsync(ToDto(state), ct);

        return Accepted(ToDto(state));
    }

    private async Task FanoutRealmStateToGameServersAsync(RealmStateDto dto, CancellationToken ct)
    {
        var sockets = gsRegistry.GetAll();
        if (sockets.Count == 0)
            return;

        var envelope = new
        {
            type = "realm.state.updated",
            payload = dto
        };

        var json = JsonConvert.SerializeObject(envelope);
        var bytes = Encoding.UTF8.GetBytes(json);

        foreach (var (gameServerId, socket) in sockets)
        {
            if (socket == null || socket.State != WebSocketState.Open)
                continue;

            try
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Realm] Failed to push realm state to GS {GameServerId}.", gameServerId);
            }
        }
    }

    private static RealmStateDto ToDto(RealmState state)
    {
        return new RealmStateDto
        {
            denyNewLogins = state.DenyNewLogins,
            denyNewJoins = state.DenyNewJoins,
            shutdownAtUnixUtc = state.ShutdownAtUtc?.ToUnixTimeSeconds(),
            realmMessage = state.ShutdownAtUtc.HasValue
                ? "Server restart scheduled" // if you have it on RealmState
                : (state.DenyNewLogins ? "Server in maintenance mode" : null)
        };
    }
}

// DTOs
public sealed record ScheduleRealmShutdownRequest(int Minutes, string Message, string InitiatedBy);
public sealed record ClearRealmRequest(string InitiatedBy);
