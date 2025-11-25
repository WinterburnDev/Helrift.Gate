using Helrift.Gate.Api.Services.Accounts;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers.Admin;

[ApiController]
[Route("admin/api/[controller]")]
public class BansController : ControllerBase
{
    private readonly IBanService _banService;

    public BansController(IBanService banService)
    {
        _banService = banService;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBanDto dto, CancellationToken ct)
    {
        if (dto == null) return BadRequest("Missing payload.");

        if (string.IsNullOrWhiteSpace(dto.RealmId))
            return BadRequest("RealmId required.");

        if (string.IsNullOrWhiteSpace(dto.SteamId) &&
            string.IsNullOrWhiteSpace(dto.IpAddress))
            return BadRequest("Either SteamId or IpAddress required.");

        if (string.IsNullOrWhiteSpace(dto.Reason))
            return BadRequest("Reason required.");

        var req = new CreateBanRequest
        {
            RealmId = dto.RealmId,
            SteamId = dto.SteamId,
            IpAddress = dto.IpAddress,
            Reason = dto.Reason,
            DurationMinutes = dto.DurationMinutes,
            CreatedBy = dto.CreatedBy
        };

        var record = await _banService.CreateBanAsync(req, ct);
        return Ok(record);
    }
}
