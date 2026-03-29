using Helrift.Gate.Contracts.Leaderboards;
using Helrift.Gate.Services.Leaderboards;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers.Admin;

[ApiController]
[Route("admin/api/leaderboards")]
public sealed class AdminLeaderboardsController(
    ILeaderboardService leaderboardService,
    ILeaderboardRepository repository) : ControllerBase
{
    /// <summary>Returns all distinct metric keys currently tracked in-memory.</summary>
    [HttpGet("keys")]
    public IActionResult GetKeys()
    {
        var keys = repository.GetDistinctMetricKeys();
        return Ok(keys);
    }

    [HttpGet("{metricKey}")]
    public async Task<ActionResult<LeaderboardResponseDto>> GetTop(
        [FromRoute] string metricKey,
        [FromQuery] string realmId = "default",
        [FromQuery] SideType side = SideType.Aresden,
        [FromQuery] LeaderboardWindowType window = LeaderboardWindowType.Weekly,
        [FromQuery] int limit = 50,
        [FromQuery] DateTime? bucketStartUtc = null,
        CancellationToken ct = default)
    {
        var result = await leaderboardService.GetTopAsync(new GetLeaderboardQuery(
            RealmId: realmId,
            Side: side,
            MetricKey: metricKey,
            Window: window,
            Limit: limit,
            BucketStartUtc: bucketStartUtc), ct);
        return Ok(result);
    }
}