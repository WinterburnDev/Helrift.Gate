using Helrift.Gate.Contracts.Leaderboards;
using Helrift.Gate.Services.Leaderboards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Helrift.Gate.Controllers;

[ApiController]
[Route("api/leaderboards")]
[Authorize]
public sealed class LeaderboardsController : ControllerBase
{
    private readonly ILeaderboardService _svc;

    public LeaderboardsController(ILeaderboardService svc)
    {
        _svc = svc;
    }

    // Public endpoint - you said it's fine to be unauthenticated
    [HttpGet("/api/leaderboards/{metricKey}")]
    public async Task<ActionResult<LeaderboardResponseDto>> GetTop(
        [FromRoute] string metricKey,
        [FromQuery] string realmId,
        [FromQuery] SideType side,
        [FromQuery] LeaderboardWindowType window,
        [FromQuery] int limit = 50,
        [FromQuery] DateTime? bucketStartUtc = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(realmId))
            realmId = "default";

        var query = new GetLeaderboardQuery(
            RealmId: realmId,
            Side: side,
            MetricKey: metricKey,
            Window: window,
            Limit: limit,
            BucketStartUtc: bucketStartUtc);

        var result = await _svc.GetTopAsync(query, ct);
        return Ok(result);
    }

    // Internal ingestion endpoint - should be protected (GS -> Gate)
    [HttpPost("increment")]
    [Authorize(Policy = "ServerOnly")]
    public async Task<IActionResult> Increment([FromBody] LeaderboardIncrementData data, CancellationToken ct = default)
    {
        await _svc.IngestAsync(data, ct);
        return Accepted();
    }
}
