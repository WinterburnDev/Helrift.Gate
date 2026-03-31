using Helrift.Gate.Api.Services.Deliveries;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers;

[ApiController]
[Route("api/v1/deliveries")]
[Authorize(Policy = "ServerOnly")]
public sealed class DeliveriesController(IDeliveryService service) : ControllerBase
{
    [HttpPost("player-message")]
    public async Task<ActionResult<DeliveryRecord>> CreatePlayerMessage([FromBody] CreatePlayerMessageRequest request, CancellationToken ct)
        => Ok(await service.CreatePlayerMessageAsync(request, ct));

    [HttpPost("parcel")]
    public async Task<ActionResult<DeliveryRecord>> CreateParcel([FromBody] CreateParcelDeliveryRequest request, CancellationToken ct)
        => Ok(await service.CreateParcelDeliveryAsync(request, ct));

    [HttpPost("system")]
    public async Task<ActionResult<IReadOnlyList<DeliveryRecord>>> CreateSystem([FromBody] CreateSystemDeliveryRequest request, CancellationToken ct)
        => Ok(await service.CreateSystemDeliveryAsync(request, ct));

    [HttpPost("guild-broadcast")]
    public async Task<ActionResult<IReadOnlyList<DeliveryRecord>>> CreateGuildBroadcast([FromBody] CreateGuildBroadcastRequest request, CancellationToken ct)
        => Ok(await service.CreateGuildBroadcastAsync(request, ct));

    [HttpGet("inbox/{recipientCharacterId}/summary")]
    public async Task<ActionResult<DeliveryInboxSummary>> GetSummary([FromRoute] string recipientCharacterId, [FromQuery] string realmId = "default", CancellationToken ct = default)
        => Ok(await service.GetInboxSummaryAsync(realmId, recipientCharacterId, ct));

    [HttpGet("inbox/{recipientCharacterId}/notifications")]
    public async Task<ActionResult<DeliveryNotificationSummary>> GetNotifications([FromRoute] string recipientCharacterId, [FromQuery] string realmId = "default", CancellationToken ct = default)
        => Ok(await service.GetNotificationSummaryAsync(realmId, recipientCharacterId, ct));

    [HttpGet("inbox/{recipientCharacterId}")]
    public async Task<ActionResult<IReadOnlyList<DeliveryRecord>>> GetInbox(
        [FromRoute] string recipientCharacterId,
        [FromQuery] string realmId = "default",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        [FromQuery] bool includeArchived = false,
        CancellationToken ct = default)
    {
        var query = new DeliveryListQuery
        {
            Page = page,
            PageSize = pageSize,
            IncludeArchived = includeArchived
        };

        return Ok(await service.GetInboxAsync(realmId, recipientCharacterId, query, ct));
    }

    [HttpGet("inbox/{recipientCharacterId}/{deliveryId}")]
    public async Task<ActionResult<DeliveryRecord>> GetDetail(
        [FromRoute] string recipientCharacterId,
        [FromRoute] string deliveryId,
        [FromQuery] string realmId = "default",
        CancellationToken ct = default)
    {
        var item = await service.GetDeliveryAsync(realmId, recipientCharacterId, deliveryId, ct);
        return item == null ? NotFound() : Ok(item);
    }

    [HttpPost("inbox/read")]
    public async Task<ActionResult<DeliveryRecord>> MarkRead([FromBody] MarkDeliveryReadRequest request, CancellationToken ct)
        => Ok(await service.MarkReadAsync(request, ct));

    [HttpPost("inbox/claim")]
    public async Task<ActionResult<DeliveryRecord>> Claim([FromBody] ClaimDeliveryAssetsRequest request, CancellationToken ct)
        => Ok(await service.ClaimAssetsAsync(request, ct));
}