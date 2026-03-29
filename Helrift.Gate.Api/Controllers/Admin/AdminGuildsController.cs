using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers.Admin;

[ApiController]
[Route("admin/api/guilds")]
public sealed class AdminGuildsController(
    IGuildDataProvider guilds,
    IServiceScopeFactory scopeFactory) : ControllerBase
{
    /// <summary>Returns a guild by ID with member character names resolved.</summary>
    [HttpGet("{guildId}")]
    public async Task<IActionResult> Get(string guildId, CancellationToken ct)
    {
        var guild = await guilds.GetAsync(guildId, ct);
        if (guild is null) return NotFound();

        var memberIds = guild.MemberCharacterIds ?? [];

        IReadOnlyDictionary<string, string> nameMap;
        if (memberIds.Count > 0)
        {
            // IGameDataProvider is Scoped — create a scope to resolve it safely
            await using var scope = scopeFactory.CreateAsyncScope();
            var gameData = scope.ServiceProvider.GetRequiredService<IGameDataProvider>();
            nameMap = await gameData.GetCharacterNamesByIdsAsync("default", memberIds, ct);
        }
        else
        {
            nameMap = new Dictionary<string, string>();
        }

        var enriched = new AdminGuildDto
        {
            GuildId = guild.GuildId,
            Name = guild.Name,
            Side = guild.Side,
            LeaderCharacterId = guild.LeaderCharacterId,
            LeaderCharacterName = nameMap.GetValueOrDefault(guild.LeaderCharacterId),
            CreatedAt = guild.CreatedAt,
            MOTD = guild.MOTD,
            Description = guild.Description,
            Emblem = guild.Emblem,
            Members = memberIds.Select(id => new AdminGuildMemberDto
            {
                CharacterId = id,
                CharacterName = nameMap.GetValueOrDefault(id),
                IsLeader = id == guild.LeaderCharacterId
            }).OrderByDescending(m => m.IsLeader).ThenBy(m => m.CharacterName).ToList()
        };

        return Ok(enriched);
    }
}

public sealed class AdminGuildDto
{
    public string GuildId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Side { get; set; } = "";
    public string LeaderCharacterId { get; set; } = "";
    public string? LeaderCharacterName { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? MOTD { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, object>? Emblem { get; set; }
    public List<AdminGuildMemberDto> Members { get; set; } = [];
}

public sealed class AdminGuildMemberDto
{
    public string CharacterId { get; set; } = "";
    public string? CharacterName { get; set; }
    public bool IsLeader { get; set; }
}