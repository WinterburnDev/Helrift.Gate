// Helrift.Gate.Api/Controllers/MerchantsController.cs
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers;

[ApiController]
[Route("api/v1/merchants/{npcId}")]
[Produces("application/json")]
public sealed class MerchantsController(IMerchantDataProvider data) : ControllerBase
{
    // --- Query ---

    [HttpPost("query")]
    [Consumes("application/json")]
    public async Task<ActionResult<MerchantPageResult>> Query(
        string npcId,
        [FromBody] MerchantQuery q,
        CancellationToken ct)
        => Ok(await data.QueryAsync(npcId, q, ct));

    // --- Single listing fetch ---

    [HttpGet("listings/{listingId}")]
    public async Task<ActionResult<MerchantItemRow>> GetListing(
        string npcId,
        string listingId,
        CancellationToken ct)
        => (await data.GetAsync(npcId, listingId, ct)) is { } row ? Ok(row) : NotFound();

    // --- Insert listing (returns JSON) ---

    public sealed record CreateListingResponse(string ListingId);

    [HttpPost("listings")]
    [Consumes("application/json")]
    public async Task<ActionResult<CreateListingResponse>> Insert(
        string npcId,
        [FromBody] MerchantItemRow row,
        CancellationToken ct)
    {
        var id = await data.TryInsertAsync(npcId, row, ct);
        if (string.IsNullOrWhiteSpace(id))
            return Conflict(new { error = "Insert failed." });

        // Ensure JSON (not text/plain) so Unity client can deserialize reliably
        return Ok(new CreateListingResponse(id));
    }

    // --- Delete listing (passes expectedOwner through) ---

    [HttpDelete("listings/{listingId}")]
    public async Task<IActionResult> Delete(
        string npcId,
        string listingId,
        [FromQuery] string? expectedOwner,
        CancellationToken ct)
        => await data.TryDeleteAsync(npcId, listingId, ct: ct) ? NoContent() : NotFound();

    // If you actually intend to enforce the expectedOwner server-side, change to:
    // => await data.TryDeleteAsync(npcId, listingId, expectedOwner, ct) ? NoContent() : NotFound();

    // --- Quantity adjustments ---

    [HttpPost("listings/{listingId}/decrement")]
    public async Task<ActionResult<object>> Decrement(
        string npcId,
        string listingId,
        [FromQuery] int count,
        CancellationToken ct)
    {
        var (ok, newQty) = await data.TryDecrementQuantityOrDeleteAsync(npcId, listingId, count, ct);
        return Ok(new { ok, newQuantity = newQty });
    }

    [HttpPost("listings/{listingId}/increment")]
    public async Task<ActionResult<object>> Increment(
        string npcId,
        string listingId,
        [FromQuery] int delta,
        CancellationToken ct)
    {
        var ok = await data.TryIncrementQuantityAsync(npcId, listingId, delta, ct);
        return Ok(new { ok });
    }

    // --- Merge stackables ---

    public sealed class MergeRequest
    {
        public required MerchantItemRow Row { get; init; }
        public int MaxStack { get; init; } // currently unused by provider; keep for parity
    }

    [HttpPost("merge")]
    [Consumes("application/json")]
    public async Task<ActionResult<object>> Merge(
        string npcId,
        [FromBody] MergeRequest req,
        CancellationToken ct)
    {
        var (merged, listingId) = await data.TryMergeStackableAsync(npcId, req.Row, ct);
        return Ok(new { merged, listingId });
    }

    // --- Maintenance ---

    [HttpPost("purge-expired")]
    public async Task<ActionResult<object>> PurgeExpired(
        string npcId,
        [FromQuery] long now,
        [FromQuery] int maxBatch = 200,
        CancellationToken ct = default)
        => Ok(new { count = await data.DeleteExpiredAsync(npcId, now, maxBatch, ct) });

    [HttpPost("trim-overflow")]
    public async Task<ActionResult<object>> TrimOverflow(
        string npcId,
        [FromQuery] int maxItems,
        [FromQuery] long now,
        CancellationToken ct = default)
        => Ok(new { count = await data.TrimOverflowAsync(npcId, maxItems, now, ct) });

    [HttpGet("merge-index")]
    public async Task<ActionResult<IReadOnlyList<MerchantItemRow>>> MergeIndex(
        string npcId,
        [FromQuery] long now,
        CancellationToken ct)
        => Ok(await data.GetAllForMergeAsync(npcId, now, ct));
}
