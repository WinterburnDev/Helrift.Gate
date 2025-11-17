using Helrift.Gate.App.Repositories;
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
        return (await data.GetCharacterAsync(accountId, charId, ct)) is { } c ? Ok(c) : NotFound();
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromRoute] string accountId, [FromBody] CharacterData c, CancellationToken ct)
    {
        if (c is null) return BadRequest("Body required.");
        if (!string.Equals((string)c.Username, accountId, StringComparison.Ordinal))
            return BadRequest("Username/accountId mismatch.");

        CharacterData saveData = new CharacterData()
        {
            Username = accountId,
            Level = 1,
            MapId = "travellerzone",
            Position = new Vec3 { x = -367.529327f, y = 5, z = -326.01825f },
            Hp = 100,
            Mp = 100,
            Sp = 100,
            Inventory = new CharacterItemData[]
            { 
                new CharacterItemData() { UniqueId = Guid.NewGuid().ToString(), ItemId = "vest", IsEquipped = true, EquipmentSlot = EquipmentSlot.Body, Quantity = 1, Endurance = 9999, Quality = ItemQuality.Flimsy },
                new CharacterItemData() { UniqueId = Guid.NewGuid().ToString(), ItemId = "trousers", IsEquipped = true, EquipmentSlot = EquipmentSlot.Legs, Quantity = 1, Endurance = 9999, Quality = ItemQuality.Flimsy },
                new CharacterItemData() { UniqueId = Guid.NewGuid().ToString(), ItemId = "wraps", IsEquipped = true, EquipmentSlot = EquipmentSlot.Arms, Quantity = 1, Endurance = 9999, Quality = ItemQuality.Flimsy },
                new CharacterItemData() { UniqueId = Guid.NewGuid().ToString(), ItemId = "woodenstaff", IsEquipped = true, EquipmentSlot = EquipmentSlot.Weapon, Quantity = 1, Endurance = 9999, Quality = ItemQuality.Flimsy }, 
                new CharacterItemData() { UniqueId = Guid.NewGuid().ToString(), ItemId = "dagger", Quantity = 1, IsEquipped = false, EquipmentSlot = EquipmentSlot.None, Endurance = 9999, Quality = ItemQuality.Flimsy }, 
                new CharacterItemData() { UniqueId = Guid.NewGuid().ToString(), ItemId = "healthpot", Quantity = 3, IsEquipped = false, Endurance = 1 } },
            CharacterName = c.CharacterName,
            Gender = c.Gender,
            Appearance = new HumanAppearance { HairStyle = c.Appearance.HairStyle, HairColour = c.Appearance.HairColour, FacialHairStyle = c.Appearance.FacialHairStyle, SkinColour = c.Appearance.SkinColour, EyeColour = c.Appearance.EyeColour, EyebrowStyle = c.Appearance.EyebrowStyle, BodyData = c.Appearance.BodyData, },
            Strength = c.Strength,
            Dexterity = c.Dexterity,
            Vitality = c.Vitality,
            Intelligence = c.Intelligence,
            Magic = c.Magic,
            Finesse = c.Finesse,
            Quests = new CharacterQuestData() { activeQuests = new CharacterQuestItemData[] { new CharacterQuestItemData() { questId = "tutorial-1", isTracked = true } } },
            Cosmetics = new CharacterCosmeticData() { activeCastEffect = "standard" }
        };

        await data.CreateCharacterAsync(saveData, ct);

        return Ok();
    }

    [HttpPut("{charId}")]
    [Authorize(Policy = "ServerOnly")]
    public async Task<IActionResult> Save([FromRoute] string accountId, [FromRoute] string charId, [FromBody] CharacterData c, CancellationToken ct)
    {
        if (c is null) return BadRequest("Body required.");
        if (!string.Equals(c.Username, accountId, StringComparison.Ordinal)) return BadRequest("Username/accountId mismatch.");
        if (!string.Equals(c.Id, charId, StringComparison.Ordinal)) return BadRequest("Id/charId mismatch.");
        await data.SaveCharacterAsync(c, ct);
        return NoContent();
    }

    [HttpDelete("{charId}")]
    [Authorize]
    public async Task<ActionResult<CharacterDeletionResult>> Delete(
    [FromRoute] string accountId,
    [FromRoute] string charId,
    CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(charId))
            return Ok(new CharacterDeletionResult
            {
                State = CharacterDeletionState.GeneralFailure
            });

        var character = await data.GetCharacterAsync(accountId, charId, ct);
        if (character == null)
            return Ok(new CharacterDeletionResult
            {
                State = CharacterDeletionState.NoCharacterExists
            });

        if ((character.Inventory != null && character.Inventory.Length > 0)
            || (character.Warehouse != null && character.Warehouse.Length > 0))
        {
            return Ok(new CharacterDeletionResult
            {
                State = CharacterDeletionState.FailedHasItems
            });
        }

        await data.DeleteCharacterAsync(accountId, charId, character.CharacterName, ct);

        return Ok(new CharacterDeletionResult
        {
            State = CharacterDeletionState.Success
        });
    }
}
