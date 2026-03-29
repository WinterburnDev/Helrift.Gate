using Helrift.Gate.Api.Services;
using Helrift.Gate.App.Domain;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers.Admin;

[ApiController]
[Route("admin/api/parties")]
public sealed class AdminPartiesController(IPartyService partyService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListAll([FromQuery] OwnerSide side = OwnerSide.Any, CancellationToken ct = default)
    {
        var parties = await partyService.ListPartiesAsync(side, ct);
        var dtos = parties.Select(PartyMapper.ToDto).ToList();
        return Ok(dtos);
    }
}