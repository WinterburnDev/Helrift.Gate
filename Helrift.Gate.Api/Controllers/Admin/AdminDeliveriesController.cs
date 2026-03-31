using Helrift.Gate.Api.Services.Deliveries;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers.Admin;

[ApiController]
[Route("admin/api/deliveries")]
public sealed class AdminDeliveriesController(IDeliveryService deliveries) : ControllerBase
{
    [HttpGet("search")]
    public async Task<ActionResult<DeliveryAdminSearchResult>> Search(
        [FromQuery] string realmId = "default",
        [FromQuery] string? characterId = null,
        [FromQuery] string? sender = null,
        [FromQuery] DeliveryType? type = null,
        [FromQuery] DateTime? createdFromUtc = null,
        [FromQuery] DateTime? createdToUtc = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = new DeliveryAdminSearchQuery
        {
            RealmId = realmId,
            CharacterId = characterId,
            SenderContains = sender,
            Type = type,
            CreatedFromUtc = createdFromUtc,
            CreatedToUtc = createdToUtc,
            Page = page,
            PageSize = pageSize
        };

        return Ok(await deliveries.SearchAdminAsync(query, ct));
    }

    [HttpGet("detail/{deliveryId}")]
    public async Task<ActionResult<DeliveryAdminDetail>> Detail(
        [FromRoute] string deliveryId,
        [FromQuery] string realmId = "default",
        CancellationToken ct = default)
    {
        var detail = await deliveries.GetAdminDetailAsync(realmId, deliveryId, ct);
        return detail == null ? NotFound() : Ok(detail);
    }

    [HttpPost("system")]
    public async Task<ActionResult<IReadOnlyList<DeliveryRecord>>> CreateSystem(
        [FromBody] CreateSystemDeliveryRequest request,
        CancellationToken ct)
        => Ok(await deliveries.CreateSystemDeliveryAsync(request, ct));

    [HttpPost("guild-broadcast")]
    public async Task<ActionResult<IReadOnlyList<DeliveryRecord>>> CreateGuildBroadcast(
        [FromBody] CreateGuildBroadcastRequest request,
        CancellationToken ct)
        => Ok(await deliveries.CreateGuildBroadcastAsync(request, ct));

    [HttpDelete("{deliveryId}")]
    public async Task<IActionResult> Delete(
        [FromRoute] string deliveryId,
        [FromQuery] string realmId = "default",
        [FromQuery] bool cleanupEscrow = true,
        [FromQuery] bool force = false,
        CancellationToken ct = default)
    {
        try
        {
            var deleted = await deliveries.DeleteAdminAsync(realmId, deliveryId, cleanupEscrow, force, ct);
            return deleted ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }
}