using Helrift.Gate.Api.Services.Bounties;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers.Admin;

[ApiController]
[Route("admin/api/bounties")]
public sealed class AdminBountiesController(IBountyService bounties) : ControllerBase
{
    [HttpGet("search")]
    public async Task<ActionResult<BountyAdminSearchResult>> Search(
        [FromQuery] string realmId = "default",
        [FromQuery] BountyStatus? status = null,
        [FromQuery] string? targetCharacterId = null,
        [FromQuery] string? issuerCharacterId = null,
        [FromQuery] string? fulfilledByCharacterId = null,
        [FromQuery] DateTime? createdFromUtc = null,
        [FromQuery] DateTime? createdToUtc = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        var query = new BountyAdminSearchQuery
        {
            RealmId = realmId,
            Status = status,
            TargetCharacterId = targetCharacterId,
            IssuerCharacterId = issuerCharacterId,
            FulfilledByCharacterId = fulfilledByCharacterId,
            CreatedFromUtc = createdFromUtc,
            CreatedToUtc = createdToUtc,
            Page = page,
            PageSize = pageSize
        };

        return Ok(await bounties.SearchAdminAsync(query, ct));
    }

    [HttpGet("detail/{bountyId}")]
    public async Task<ActionResult<BountyAdminDetail>> Detail(
        [FromRoute] string bountyId,
        [FromQuery] string realmId = "default",
        CancellationToken ct = default)
    {
        var detail = await bounties.GetAdminDetailAsync(realmId, bountyId, ct);
        return detail == null ? NotFound() : Ok(detail);
    }
}
