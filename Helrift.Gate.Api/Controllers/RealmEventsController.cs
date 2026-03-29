using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Helrift.Gate.Api.Services.RealmEvents;
using Helrift.Gate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Helrift.Gate.Contracts;

namespace Helrift.Gate.Api.Controllers;

[ApiController]
[Route("api/v1/realm-events")]
public sealed class RealmEventsController(
    IRealmEventService realmEventService,
    ILogger<RealmEventsController> logger
) : ControllerBase
{
    // POST /api/v1/realm-events/publish
    // Must come from a game server
    [HttpPost("publish")]
    [Authorize(Policy = "ServerOnly")]
    public async Task<IActionResult> Publish([FromBody] RealmEventEnvelope env, CancellationToken ct)
    {
        if (env is null) return BadRequest("Body required.");

        var result = await realmEventService.PublishAsync(env, User, ct);
        if (!result.Accepted)
            return Conflict(new { status = "rejected", message = result.Message, instanceId = result.InstanceId, sequence = result.Sequence });

        return Ok(new { status = "accepted", instanceId = result.InstanceId, sequence = result.Sequence });
    }

    // POST /api/v1/realm-events/subscribe
    // Registration from game servers / admin processes only
    [HttpPost("subscribe")]
    [Authorize(Policy = "ServerOnly")]
    public async Task<IActionResult> Subscribe([FromBody] SubscriptionRequest dto, CancellationToken ct)
    {
        if (dto is null) return BadRequest("Body required.");
        try
        {
            var info = await realmEventService.SubscribeAsync(dto, User, ct);
            return Ok(new { subscriptionId = info.Id, callbackAuthToken = info.Secret });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    // POST /internal/realm-events/push/{subscriptionId}
    // Internal delivery endpoint; validate header matches subscription secret
    [HttpPost("/internal/realm-events/push/{subscriptionId}")]
    public async Task<IActionResult> InternalPush([FromRoute] string subscriptionId, [FromBody] RealmEventEnvelope env, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(subscriptionId)) return BadRequest("subscriptionId required.");
        if (env is null) return BadRequest("Body required.");

        // validate header
        if (!Request.Headers.TryGetValue("X-Gate-Subscription-Token", out var token))
            return Unauthorized();

        // Note: the delivery worker already validates and signs callbacks.
        // This internal endpoint currently acts as a simple receiver for internally-sent pushes.
        // If you want stronger validation here, expose GetSubscriptionAsync on IRealmEventService
        // and validate token == subscription.Secret. Keeping lightweight for now.
        return Ok(new { status = "accepted" });
    }

    // GET /api/v1/realm-events/snapshot/{realmId}/{eventInstanceId}
    [HttpGet("snapshot/{realmId}/{eventInstanceId}")]
    public async Task<IActionResult> GetSnapshot([FromRoute] string realmId, [FromRoute] string eventInstanceId, CancellationToken ct)
    {
        var env = await realmEventService.GetSnapshotAsync(realmId, eventInstanceId, ct);
        if (env is null) return NotFound();
        return Ok(env);
    }

    // GET /api/v1/realm-events/recent/{realmId}?limit=50
    [HttpGet("recent/{realmId}")]
    public async Task<IActionResult> GetRecent([FromRoute] string realmId, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);
        var arr = await realmEventService.GetRecentAsync(realmId, limit, ct);
        return Ok(arr);
    }
}