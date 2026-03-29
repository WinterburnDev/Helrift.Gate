using Helrift.Gate.Api.Services.GameServers;
using Helrift.Gate.Api.Services.GameServers.Models;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers
{
    [ApiController]
    [Route("api/v1/gameservers")]
    [Authorize(Policy = "ServerOnly")]
    public sealed class GameServerRegistrationController(
        IGameServerRegistrationRegistry registry,
        ILogger<GameServerRegistrationController> logger) : ControllerBase
    {
        [HttpPost("register")]
        public IActionResult Register([FromBody] GameServerRegistrationDto dto)
        {
            if (dto == null)
                return BadRequest("Body required.");

            if (string.IsNullOrWhiteSpace(dto.gameServerId))
                return BadRequest("gameServerId required.");

            var registration = registry.Upsert(dto);

            logger.LogInformation(
                "Game server registration received: {GameServerId}, build {BuildVersion}, maps {MapCount}",
                registration.GameServerId,
                registration.BuildVersion,
                registration.Maps.Count);

            return Ok();
        }

        [HttpGet("{id}")]
        public ActionResult<GameServerRegistration> Get([FromRoute] string id)
        {
            var registration = registry.Get(id);
            return registration is null ? NotFound() : Ok(registration);
        }
    }
}