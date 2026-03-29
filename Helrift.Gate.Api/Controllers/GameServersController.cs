using Helrift.Gate.Api.Services.GameServers;
using Helrift.Gate.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using System.Net.WebSockets;

namespace Helrift.Gate.Api.Controllers;

[ApiController]
[Route("admin/api/[controller]")]
public class GameServersController : ControllerBase
{
    private readonly IGameServerConnectionRegistry _connections;
    private readonly IGameServerRegistrationRegistry _registrations;

    public GameServersController(
        IGameServerConnectionRegistry connections,
        IGameServerRegistrationRegistry registrations)
    {
        _connections = connections;
        _registrations = registrations;
    }

    public sealed class GameServerMapDto
    {
        public string Id { get; set; } = "";
        public string MapName { get; set; } = "";
        public string SceneName { get; set; } = "";
        public int CellX { get; set; }
        public int CellY { get; set; }
        public bool IsSafeMap { get; set; }
        public bool IsOutside { get; set; }
    }

    public sealed class GameServerDto
    {
        public string Id { get; set; } = "";
        public string State { get; set; } = "";
        public bool IsConnected { get; set; }
        public string BuildVersion { get; set; } = "";
        public long? RegisteredAtUnixUtc { get; set; }
        public DateTime? LastHeartbeatUtc { get; set; }
        public int MapCount { get; set; }
        public IReadOnlyList<GameServerMapDto> Maps { get; set; } = Array.Empty<GameServerMapDto>();
    }

    [HttpGet]
    public ActionResult<IEnumerable<GameServerDto>> Get()
    {
        var socketsById = _connections.GetAll();
        var registrationsById = _registrations
            .GetAll()
            .ToDictionary(r => r.GameServerId, StringComparer.Ordinal);

        var allIds = socketsById.Keys
            .Union(registrationsById.Keys, StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var servers = allIds
            .Select(id =>
            {
                socketsById.TryGetValue(id, out var socket);
                registrationsById.TryGetValue(id, out var registration);
                return ToDto(id, socket, registration);
            })
            .ToList();

        return Ok(servers);
    }

    [HttpGet("{id}")]
    public ActionResult<GameServerDto> GetById([FromRoute] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest("id required.");

        var socketsById = _connections.GetAll();
        var socket = socketsById.TryGetValue(id, out var ws) ? ws : null;
        var registration = _registrations.Get(id);

        if (socket == null && registration == null)
            return NotFound();

        return Ok(ToDto(id, socket, registration));
    }

    private static GameServerDto ToDto(
        string id,
        WebSocket? socket,
        Services.GameServers.Models.GameServerRegistration? registration)
    {
        var maps = registration?.Maps?
            .Select(m => new GameServerMapDto
            {
                Id = m.id ?? "",
                MapName = m.mapName ?? "",
                SceneName = m.sceneName ?? "",
                CellX = m.cellX,
                CellY = m.cellY,
                IsSafeMap = m.isSafeMap,
                IsOutside = m.isOutside
            })
            .ToList() ?? new List<GameServerMapDto>();

        return new GameServerDto
        {
            Id = id,
            State = socket != null ? MapState(socket.State) : "Offline",
            IsConnected = socket?.State == WebSocketState.Open,
            BuildVersion = registration?.BuildVersion ?? "",
            RegisteredAtUnixUtc = registration?.RegisteredAtUnixUtc,
            LastHeartbeatUtc = registration?.LastHeartbeatUtc,
            MapCount = maps.Count,
            Maps = maps
        };
    }

    private static string MapState(WebSocketState state) => state switch
    {
        WebSocketState.Open => "Online",
        WebSocketState.CloseReceived => "Closing",
        WebSocketState.CloseSent => "Closing",
        WebSocketState.Closed => "Offline",
        WebSocketState.Aborted => "Aborted",
        _ => state.ToString()
    };
}
