// Helrift.Gate.Api/Controllers/EntitlementsController.cs
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers;

[ApiController]
[Route("api/v1/entitlements")]
public sealed class EntitlementsController(IEntitlementsDataProvider data) : ControllerBase
{
    public sealed class AliasResponse
    {
        public string Id { get; init; } = "";
    }

    // GET /api/v1/entitlements/aliases/current
    [HttpGet("aliases/current")]
    public async Task<ActionResult<AliasResponse>> GetCurrentAlias(CancellationToken ct)
    {
        var id = await data.GetCurrentIdAsync(ct);
        return string.IsNullOrWhiteSpace(id) ? NotFound() : Ok(new AliasResponse { Id = id! });
    }

    // GET /api/v1/entitlements/unlockables
    // Uses current alias to resolve the active entitlements set.
    [HttpGet("unlockables")]
    public async Task<ActionResult<IReadOnlyDictionary<string, EntitlementUnlockableRow>>> GetCurrentUnlockables(CancellationToken ct)
    {
        var id = await data.GetCurrentIdAsync(ct);
        if (string.IsNullOrWhiteSpace(id)) return NotFound();

        var rows = await data.GetUnlockablesByIdAsync(id!, ct);
        return Ok(rows);
    }

    // GET /api/v1/entitlements/{entitlementsId}/unlockables
    [HttpGet("{entitlementsId}/unlockables")]
    public async Task<ActionResult<IReadOnlyDictionary<string, EntitlementUnlockableRow>>> GetUnlockablesById(
        [FromRoute] string entitlementsId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entitlementsId)) return BadRequest("entitlementsId required.");
        var rows = await data.GetUnlockablesByIdAsync(entitlementsId, ct);
        return Ok(rows);
    }
}
