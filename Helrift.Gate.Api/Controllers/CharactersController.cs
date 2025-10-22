using Helrift.Gate.App;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/v1/accounts/{accountId}/characters")]
public sealed class CharactersController(IGameDataProvider data) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<CharacterData>>> List([FromRoute] string accountId, CancellationToken ct)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!string.Equals(sub, accountId, StringComparison.Ordinal))
            return Forbid();

        return Ok(await data.GetCharactersAsync(accountId, ct));
    }

    [HttpGet("{charId}")]
    [Authorize(Policy = "ServerOnly")]
    public async Task<ActionResult<CharacterData>> Get([FromRoute] string accountId, [FromRoute] string charId, CancellationToken ct)
    {
        //var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        //if (!string.Equals(sub, accountId, StringComparison.Ordinal))
        //    return Forbid();

        return (await data.GetCharacterAsync(accountId, charId, ct)) is { } c ? Ok(c) : NotFound();
    }

    [HttpPost]
    [Authorize(Policy = "ServerOnly")]
    public async Task<IActionResult> Create([FromRoute] string accountId, [FromBody] CharacterData c, CancellationToken ct)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!string.Equals(sub, accountId, StringComparison.Ordinal))
            return Forbid();

        if (c is null) return BadRequest("Body required.");
        if (!string.Equals(c.Username, accountId, StringComparison.Ordinal))
            return BadRequest("Username/accountId mismatch.");

        await data.CreateCharacterAsync(c, ct);
        return CreatedAtAction(nameof(Get), new { accountId, charId = c.Id }, null);
    }

    [HttpPut("{charId}")]
    [Authorize(Policy = "ServerOnly")]
    public async Task<IActionResult> Save([FromRoute] string accountId, [FromRoute] string charId, [FromBody] CharacterData c, CancellationToken ct)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!string.Equals(sub, accountId, StringComparison.Ordinal))
            return Forbid();

        if (c is null) return BadRequest("Body required.");
        if (!string.Equals(c.Username, accountId, StringComparison.Ordinal)) return BadRequest("Username/accountId mismatch.");
        if (!string.Equals(c.Id, charId, StringComparison.Ordinal)) return BadRequest("Id/charId mismatch.");
        await data.SaveCharacterAsync(c, ct);
        return NoContent();
    }

    [HttpDelete("{charId}")]
    [Authorize(Policy = "ServerOnly")]
    public async Task<IActionResult> Delete([FromRoute] string accountId, [FromRoute] string charId, [FromQuery] string name, CancellationToken ct)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!string.Equals(sub, accountId, StringComparison.Ordinal))
            return Forbid();

        if (string.IsNullOrWhiteSpace(name)) return BadRequest("Missing 'name' query parameter.");
        await data.DeleteCharacterAsync(accountId, charId, name, ct);
        return NoContent();
    }
}
