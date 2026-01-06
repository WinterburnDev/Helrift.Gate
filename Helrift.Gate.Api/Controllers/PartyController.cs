using Helrift.Gate.Api.Services;
using Helrift.Gate.App.Domain;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Helrift.Gate.Api.Controllers
{
    [ApiController]
    [Route("api/party")]
    [Authorize]
    public class PartyController : ControllerBase
    {
        private readonly IPartyService _partyService;
        private readonly IPartyExperienceService _experienceService;

        public PartyController(IPartyService partyService, IPartyExperienceService experienceService)
        {
            _partyService = partyService;
            _experienceService = experienceService;
        }

        private bool IsAccountOwner(string accountId)
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return string.Equals(sub, accountId, StringComparison.Ordinal);
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
        [Authorize]
        public async Task<ActionResult<List<PartyDto>>> ListParties(
        [FromQuery] OwnerSide side,
        [FromQuery] string? accountId,
        [FromQuery] string? characterId,
        CancellationToken ct)
        {
            if (!IsAccountOwner(accountId)) return Forbid();

            var parties = await _partyService.ListVisiblePartiesAsync(side, accountId, characterId, ct);
            var list = parties.Select(PartyMapper.ToDto).ToList();
            return Ok(list);
        }

        [HttpGet("listall")]
        public async Task<ActionResult<IEnumerable<PartyDto>>> ListAll(
            [FromQuery] OwnerSide side = OwnerSide.Any,
            CancellationToken ct = default)
        {
            var parties = await _partyService.ListPartiesAsync(side, ct);
            var dtos = parties.Select(PartyMapper.ToDto).ToList();
            return Ok(dtos);
        }

        [HttpPost("experience")]
        [Authorize(Policy = "ServerOnly")]
        public async Task<IActionResult> AddExperience([FromBody] PartyExperienceEventBatchDto batch, CancellationToken ct)
        {
            if (batch?.Events == null || batch.Events.Count == 0)
                return Accepted();

            await _experienceService.ProcessBatchAsync(batch, ct);
            return Accepted();
        }
    }
}
