// Helrift.Gate.Api/Controllers/GuildsController.cs
using Helrift.Gate.Adapters.Firebase;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Mvc;
using Helrift.Gate.App.Repositories;

namespace Helrift.Gate.Api.Controllers;

[ApiController]
[Route("api/v1/guilds")]
public sealed class GuildsController(IGuildDataProvider data) : ControllerBase
{
    // GET /api/v1/guilds/{guildId}
    [HttpGet("{guildId}")]
    public async Task<ActionResult<GuildData>> Get([FromRoute] string guildId, CancellationToken ct)
        => (await data.GetAsync(guildId, ct)) is { } g ? Ok(g) : NotFound();

    // GET /api/v1/guilds?side=Light&q=foo
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GuildData>>> Query([FromQuery] string? side, [FromQuery] string? q, CancellationToken ct)
        => Ok(await data.QueryAsync(side, q, ct));

    // PUT /api/v1/guilds/{guildId}
    [HttpPut("{guildId}")]
    public async Task<IActionResult> Save([FromRoute] string guildId, [FromBody] GuildData g, CancellationToken ct)
    {
        if (g is null) return BadRequest("Body required.");
        if (!string.Equals(g.GuildId, guildId, StringComparison.Ordinal))
            return BadRequest("GuildId mismatch.");

        var ok = await data.SaveAsync(g, ct);
        return ok ? NoContent() : Problem("Save failed.");
    }

    // DELETE /api/v1/guilds/{guildId}
    [HttpDelete("{guildId}")]
    public async Task<IActionResult> Delete([FromRoute] string guildId, CancellationToken ct)
        => await data.DeleteAsync(guildId, ct) ? NoContent() : NotFound();
}
