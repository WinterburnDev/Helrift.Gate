using Helrift.Gate.Api.Services.Marketplace;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers.Admin;

[ApiController]
[Route("admin/api/marketplace")]
public sealed class AdminMarketplaceController(IMarketplaceService marketplace) : ControllerBase
{
    [HttpGet("search")]
    public async Task<ActionResult<MarketplaceAdminSearchResult>> Search(
        [FromQuery] string realmId = "default",
        [FromQuery] MarketplaceOrderType? orderType = null,
        [FromQuery] MarketplaceOrderStatus? status = null,
        [FromQuery] string? characterId = null,
        [FromQuery] string? itemDefinitionId = null,
        [FromQuery] DateTime? createdFromUtc = null,
        [FromQuery] DateTime? createdToUtc = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        var query = new MarketplaceAdminSearchQuery
        {
            RealmId = realmId,
            OrderType = orderType,
            Status = status,
            CharacterId = characterId,
            ItemDefinitionId = itemDefinitionId,
            CreatedFromUtc = createdFromUtc,
            CreatedToUtc = createdToUtc,
            Page = page,
            PageSize = pageSize
        };

        return Ok(await marketplace.SearchAdminAsync(query, ct));
    }

    [HttpGet("detail/{orderId}")]
    public async Task<ActionResult<MarketplaceAdminDetail>> Detail(
        [FromRoute] string orderId,
        [FromQuery] string realmId = "default",
        CancellationToken ct = default)
    {
        var detail = await marketplace.GetAdminDetailAsync(realmId, orderId, ct);
        return detail == null ? NotFound() : Ok(detail);
    }

    [HttpGet("offers/search")]
    public async Task<ActionResult<MarketplaceOfferAdminSearchResult>> SearchOffers(
        [FromQuery] string realmId = "default",
        [FromQuery] MarketplaceOfferStatus? status = null,
        [FromQuery] string? marketplaceOrderId = null,
        [FromQuery] string? characterId = null,
        [FromQuery] string? itemDefinitionId = null,
        [FromQuery] DateTime? createdFromUtc = null,
        [FromQuery] DateTime? createdToUtc = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        var query = new MarketplaceOfferAdminSearchQuery
        {
            RealmId = realmId,
            Status = status,
            MarketplaceOrderId = marketplaceOrderId,
            CharacterId = characterId,
            ItemDefinitionId = itemDefinitionId,
            CreatedFromUtc = createdFromUtc,
            CreatedToUtc = createdToUtc,
            Page = page,
            PageSize = pageSize
        };

        return Ok(await marketplace.SearchOffersAdminAsync(query, ct));
    }

    [HttpGet("offers/detail/{offerId}")]
    public async Task<ActionResult<MarketplaceOfferAdminDetail>> OfferDetail(
        [FromRoute] string offerId,
        [FromQuery] string realmId = "default",
        CancellationToken ct = default)
    {
        var detail = await marketplace.GetOfferAdminDetailAsync(realmId, offerId, ct);
        return detail == null ? NotFound() : Ok(detail);
    }
}
