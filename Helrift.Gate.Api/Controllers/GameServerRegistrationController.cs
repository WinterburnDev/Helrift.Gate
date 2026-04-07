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

        [HttpPost("weather")]
        public IActionResult UpdateWeather([FromBody] GameServerWeatherStateDto dto)
        {
            if (dto == null)
                return BadRequest("Body required.");

            if (string.IsNullOrWhiteSpace(dto.gameServerId))
                return BadRequest("gameServerId required.");

            if (string.IsNullOrWhiteSpace(dto.weatherKind))
                return BadRequest("weatherKind required.");

            var registration = registry.UpsertWeatherState(dto);

            logger.LogInformation(
                "Game server weather update received: {GameServerId}, weather {WeatherKind}, observed {ObservedAtUnixUtc}",
                registration.GameServerId,
                dto.weatherKind,
                dto.observedAtUnixUtc);

            return Ok(new
            {
                status = "accepted",
                gameServerId = registration.GameServerId,
                observedAtUnixUtc = dto.observedAtUnixUtc
            });
        }

        [HttpGet("{id}")]
        public ActionResult<GameServerRegistration> Get([FromRoute] string id)
        {
            var registration = registry.Get(id);
            return registration is null ? NotFound() : Ok(registration);
        }
    }
}