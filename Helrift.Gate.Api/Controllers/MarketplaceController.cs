using Helrift.Gate.Api.Services.Marketplace;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers;

[ApiController]
[Route("api/v1/marketplace")]
[Authorize(Policy = "ServerOnly")]
public sealed class MarketplaceController(IMarketplaceService marketplace) : ControllerBase
{
    [HttpPost("orders/sell")]
    public async Task<ActionResult<MarketplaceOrderSnapshot>> CreateSellOrder([FromBody] CreateMarketplaceSellOrderRequest request, CancellationToken ct)
        => Ok(await marketplace.CreateSellOrderAsync(request, ct));

    [HttpPost("orders/buy")]
    public async Task<ActionResult<MarketplaceOrderSnapshot>> CreateBuyOrder([FromBody] CreateMarketplaceBuyOrderRequest request, CancellationToken ct)
        => Ok(await marketplace.CreateBuyOrderAsync(request, ct));

    [HttpPost("orders/fulfill")]
    public async Task<ActionResult<MarketplaceOrderSnapshot>> Fulfill([FromBody] FulfillMarketplaceOrderRequest request, CancellationToken ct)
        => Ok(await marketplace.FulfillOrderAsync(request, ct));

    [HttpPost("orders/cancel")]
    public async Task<ActionResult<MarketplaceOrderSnapshot>> Cancel([FromBody] CancelMarketplaceOrderRequest request, CancellationToken ct)
        => Ok(await marketplace.CancelOrderAsync(request, ct));

    [HttpPost("offers")]
    public async Task<ActionResult<MarketplaceOfferSnapshot>> CreateOffer([FromBody] CreateMarketplaceOfferRequest request, CancellationToken ct)
        => Ok(await marketplace.CreateOfferAsync(request, ct));

    [HttpPost("offers/respond")]
    public async Task<ActionResult<MarketplaceOfferSnapshot>> RespondOffer([FromBody] RespondMarketplaceOfferRequest request, CancellationToken ct)
        => Ok(await marketplace.RespondOfferAsync(request, ct));

    [HttpPost("offers/cancel")]
    public async Task<ActionResult<MarketplaceOfferSnapshot>> CancelOffer([FromBody] CancelMarketplaceOfferRequest request, CancellationToken ct)
        => Ok(await marketplace.CancelOfferAsync(request, ct));

    [HttpGet("orders/browse")]
    public async Task<ActionResult<MarketplaceBrowseResult>> Browse(
        [FromQuery] string realmId = "default",
        [FromQuery] MarketplaceOrderType? orderType = null,
        [FromQuery] MarketplaceOrderStatus? status = null,
        [FromQuery] string? itemDefinitionId = null,
        [FromQuery] string? characterId = null,
        [FromQuery] string sort = "newest",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = new MarketplaceBrowseQuery
        {
            RealmId = realmId,
            OrderType = orderType,
            Status = status,
            ItemDefinitionId = itemDefinitionId,
            CharacterId = characterId,
            Sort = sort,
            Page = page,
            PageSize = pageSize
        };

        return Ok(await marketplace.BrowseAsync(query, ct));
    }

    [HttpGet("orders/my/{ownerCharacterId}")]
    public async Task<ActionResult<MarketplaceBrowseResult>> MyOrders(
        [FromRoute] string ownerCharacterId,
        [FromQuery] string realmId = "default",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await marketplace.GetMyOrdersAsync(realmId, ownerCharacterId, page, pageSize, ct));

    [HttpGet("offers/order/{orderId}")]
    public async Task<ActionResult<MarketplaceOfferBrowseResult>> OrderOffers(
        [FromRoute] string orderId,
        [FromQuery] string realmId = "default",
        [FromQuery] bool includeHistory = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await marketplace.GetOffersByOrderAsync(realmId, orderId, includeHistory, page, pageSize, ct));

    [HttpGet("offers/my/{creatorCharacterId}")]
    public async Task<ActionResult<MarketplaceOfferBrowseResult>> MyOffers(
        [FromRoute] string creatorCharacterId,
        [FromQuery] string realmId = "default",
        [FromQuery] bool includeHistory = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await marketplace.GetMyOffersAsync(realmId, creatorCharacterId, includeHistory, page, pageSize, ct));
}
