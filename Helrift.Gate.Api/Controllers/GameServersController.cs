using Helrift.Gate.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using System.Net.WebSockets;

namespace Helrift.Gate.Api.Controllers;

[ApiController]
[Route("admin/api/[controller]")]
public class GameServersController : ControllerBase
{
    private readonly IGameServerConnectionRegistry _registry;

    public GameServersController(IGameServerConnectionRegistry registry)
    {
        _registry = registry;
    }

    public sealed class GameServerDto
    {
        public string Id { get; set; } = "";
        public string State { get; set; } = "";
    }

    [HttpGet]
    public ActionResult<IEnumerable<GameServerDto>> Get()
    {
        var servers = _registry
            .GetAll()
            .Select(kvp => new GameServerDto
            {
                Id = kvp.Key,
                State = MapState(kvp.Value.State)
            })
            .ToList();

        return Ok(servers);
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
