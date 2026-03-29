using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers.Admin;

[ApiController]
[Route("admin/api/merchants")]
public sealed class AdminMerchantsController(IMerchantDataProvider data, IAdminMerchantDirectory directory) : ControllerBase
{
    /// <summary>Lists all known NPC merchant IDs.</summary>
    [HttpGet]
    public async Task<IActionResult> ListNpcs(CancellationToken ct)
    {
        var ids = await directory.GetAllNpcIdsAsync(ct);
        return Ok(ids);
    }

    /// <summary>Returns all active listings for one NPC merchant.</summary>
    [HttpGet("{npcId}")]
    public async Task<IActionResult> GetNpcListings(string npcId, CancellationToken ct)
    {
        var result = await data.QueryAsync(npcId, new MerchantQuery
        {
            Page = 1,
            PageSize = 500,
            Sort = MerchantQuerySort.ListedAtDesc,
            IncludeExpired = false
        }, ct);
        return Ok(result);
    }
}