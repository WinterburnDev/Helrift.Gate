using Helrift.Gate.Api.Services;
using Helrift.Gate.App.Domain;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers
{
    [ApiController]
    [Route("api/party")]
    public class PartyController : ControllerBase
    {
        private readonly IPartyService _partyService;

        public PartyController(IPartyService partyService)
        {
            _partyService = partyService;
        }

        [HttpPost("create")]
        public async Task<ActionResult<PartyDto>> Create([FromBody] CreatePartyRequest request, CancellationToken ct)
        {
            var dto = await _partyService.CreatePartyAsync(request, ct);
            return Ok(dto);
        }

        [HttpPost("join")]
        public async Task<ActionResult<PartyDto>> Join([FromBody] JoinPartyRequest request, CancellationToken ct)
        {
            var dto = await _partyService.JoinPartyAsync(request, ct);
            if (dto == null)
                return NotFound(); // party doesn't exist
            return Ok(dto);
        }

        [HttpPost("leave")]
        public async Task<ActionResult<PartyDto?>> Leave([FromBody] LeavePartyRequest request, CancellationToken ct)
        {
            var dto = await _partyService.LeavePartyAsync(request, ct);
            if (dto == null)
                return NoContent(); // party disbanded
            return Ok(dto);
        }

        [HttpPost("set-leader")]
        public async Task<ActionResult<PartyDto>> SetLeader([FromBody] SetLeaderRequest request, CancellationToken ct)
        {
            var dto = await _partyService.SetLeaderAsync(request, ct);
            if (dto == null)
                return NotFound();
            return Ok(dto);
        }

        [HttpPost("kick")]
        public async Task<ActionResult<PartyDto>> Kick([FromBody] KickMemberRequest request, CancellationToken ct)
        {
            var dto = await _partyService.KickMemberAsync(request, ct);
            if (dto == null)
                return NotFound();
            return Ok(dto);
        }

        [HttpGet("list")]
        public async Task<ActionResult<List<PartyDto>>> ListParties(
        [FromQuery] string side,
        [FromQuery] string? viewerCharacterId,
        CancellationToken ct)
        {
            if (!Enum.TryParse<OwnerSide>(side, ignoreCase: true, out var parsedSide))
                return BadRequest("Invalid side.");

            var parties = await _partyService.ListVisiblePartiesAsync(parsedSide, viewerCharacterId, ct);
            var list = parties.Select(PartyMapper.ToDto).ToList();
            return Ok(list);
        }
    }

}
