using Helrift.Gate.Api.Services.Bounties;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers;

[ApiController]
[Route("api/v1/bounties")]
[Authorize(Policy = "ServerOnly")]
public sealed class BountiesController(IBountyService bounties) : ControllerBase
{
    [HttpPost("create")]
    public async Task<ActionResult<BountyOrderSnapshot>> Create([FromBody] CreateBountyContractRequest request, CancellationToken ct)
        => Ok(await bounties.CreateBountyAsync(request, ct));

    [HttpPost("cancel")]
    public async Task<ActionResult<BountyOrderSnapshot>> Cancel([FromBody] CancelBountyContractRequest request, CancellationToken ct)
        => Ok(await bounties.CancelBountyAsync(request, ct));

    [HttpPost("resolve-kill")]
    public async Task<ActionResult<ResolveBountyKillResult>> ResolveKill([FromBody] ResolveBountyKillRequest request, CancellationToken ct)
        => Ok(await bounties.ResolveKillAsync(request, ct));

    [HttpGet("active")]
    public async Task<ActionResult<BountyBrowseResult>> Active(
        [FromQuery] string realmId = "default",
        [FromQuery] string? targetCharacterId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = new BountyBrowseQuery
        {
            RealmId = realmId,
            Status = BountyStatus.Active,
            TargetCharacterId = targetCharacterId,
            Page = page,
            PageSize = pageSize
        };

        return Ok(await bounties.BrowseActiveAsync(query, ct));
    }

    [HttpGet("issued/{issuerCharacterId}")]
    public async Task<ActionResult<BountyBrowseResult>> Issued(
        [FromRoute] string issuerCharacterId,
        [FromQuery] string realmId = "default",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await bounties.GetMyIssuedAsync(realmId, issuerCharacterId, page, pageSize, ct));
}
