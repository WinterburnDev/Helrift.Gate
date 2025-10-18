using Helrift.Gate.App;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers;

[ApiController]
[Route("api/v1/accounts/{accountId}/characters")]
public sealed class CharactersController(IGameDataProvider data) : ControllerBase
{
    // GET /api/v1/accounts/{accountId}/characters
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CharacterData>>> List([FromRoute] string accountId, CancellationToken ct)
        => Ok(await data.GetCharactersAsync(accountId, ct));

    // GET /api/v1/accounts/{accountId}/characters/{charId}
    [HttpGet("{charId}")]
    public async Task<ActionResult<CharacterData>> Get([FromRoute] string accountId, [FromRoute] string charId, CancellationToken ct)
        => (await data.GetCharacterAsync(accountId, charId, ct)) is { } c ? Ok(c) : NotFound();

    // POST /api/v1/accounts/{accountId}/characters
    [HttpPost]
    public async Task<IActionResult> Create([FromRoute] string accountId, [FromBody] CharacterData c, CancellationToken ct)
    {
        if (c is null) return BadRequest("Body required.");
        if (!string.Equals(c.Username, accountId, StringComparison.Ordinal))
            return BadRequest("Username/accountId mismatch.");

        await data.CreateCharacterAsync(c, ct);

        // NOTE: If CreateCharacterAsync generates the Id, ensure the caller sets c.Id
        // or have the provider return it. For now we use whatever is in c.Id.
        return CreatedAtAction(nameof(Get), new { accountId, charId = c.Id }, null);
    }

    // PUT /api/v1/accounts/{accountId}/characters/{charId}
    [HttpPut("{charId}")]
    public async Task<IActionResult> Save([FromRoute] string accountId, [FromRoute] string charId, [FromBody] CharacterData c, CancellationToken ct)
    {
        if (c is null) return BadRequest("Body required.");
        if (!string.Equals(c.Username, accountId, StringComparison.Ordinal))
            return BadRequest("Username/accountId mismatch.");
        if (!string.Equals(c.Id, charId, StringComparison.Ordinal))
            return BadRequest("Id/charId mismatch.");

        await data.SaveCharacterAsync(c, ct);
        return NoContent();
    }

    // (optional but handy)
    // DELETE /api/v1/accounts/{accountId}/characters/{charId}?name=CharacterName
    [HttpDelete("{charId}")]
    public async Task<IActionResult> Delete([FromRoute] string accountId, [FromRoute] string charId, [FromQuery] string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("Missing 'name' query parameter.");
        await data.DeleteCharacterAsync(accountId, charId, name, ct);
        return NoContent();
    }
}
