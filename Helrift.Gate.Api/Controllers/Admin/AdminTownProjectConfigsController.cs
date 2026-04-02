using Helrift.Gate.Api.Services.ConfigPlatform;
using Helrift.Gate.Api.Services.TownProjects;
using Helrift.Gate.Contracts.TownProjects;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers.Admin;

[ApiController]
[Route("admin/api/config/town-projects")]
public sealed class AdminTownProjectConfigsController(ITownProjectConfigService configService) : ControllerBase
{
    [HttpGet("runtime-metadata")]
    public IActionResult GetRuntimeMetadata()
    {
        var metadata = configService.GetConfigMetadata();
        return Ok(metadata);
    }

    [HttpGet("versions")]
    public async Task<IActionResult> ListVersions(CancellationToken ct)
    {
        var versions = await configService.ListVersionsAsync(ct);
        return Ok(new { versions });
    }

    [HttpGet("versions/{version}")]
    public async Task<IActionResult> GetVersion([FromRoute] string version, CancellationToken ct)
    {
        var config = await configService.GetVersionAsync(version, ct);
        if (config == null)
            return NotFound(new { error = $"Version '{version}' not found." });

        return Ok(config);
    }

    [HttpPost("compare")]
    public async Task<IActionResult> Compare([FromBody] CompareVersionsRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.LeftVersion) || string.IsNullOrWhiteSpace(request.RightVersion))
            return BadRequest(new { error = "Both LeftVersion and RightVersion are required." });

        try
        {
            var result = await configService.CompareVersionsAsync(request.LeftVersion, request.RightVersion, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpGet("realms")]
    public async Task<IActionResult> ListRealmSelections(CancellationToken ct)
    {
        var realms = await configService.ListRealmSelectionsAsync(ct);
        return Ok(new { realms });
    }

    [HttpGet("realms/{realmId}")]
    public async Task<IActionResult> GetRealmSelection([FromRoute] string realmId, CancellationToken ct)
    {
        var selectedVersion = await configService.GetRealmSelectionAsync(realmId, ct);
        if (string.IsNullOrWhiteSpace(selectedVersion))
            return NotFound(new { error = $"Realm '{realmId}' has no town project version selection." });

        return Ok(new RealmVersionSelection
        {
            RealmId = realmId,
            Version = selectedVersion
        });
    }

    [HttpPut("realms/{realmId}/version/{version}")]
    public async Task<IActionResult> SetRealmSelection([FromRoute] string realmId, [FromRoute] string version, CancellationToken ct)
    {
        try
        {
            await configService.SetRealmSelectionAsync(realmId, version, ct);
            return Ok(new
            {
                realmId,
                version,
                refreshPolicy = "manual",
                effectiveAfterRestart = true,
                note = "Selection is persisted. Running game servers keep existing loaded config until restart/redeploy."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] TownProjectConfigRoot config, CancellationToken ct)
    {
        var validation = await configService.ValidateAsync(config, ct);
        return Ok(validation);
    }

    [HttpPut("versions/{version}")]
    public async Task<IActionResult> SaveVersion([FromRoute] string version, [FromBody] TownProjectConfigRoot config, CancellationToken ct)
    {
        if (config == null)
            return BadRequest(new { error = "Config payload is required." });

        if (string.IsNullOrWhiteSpace(config.Version))
            config = config with { Version = version };

        if (!string.Equals(config.Version, version, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Route version and payload version must match." });

        var save = await configService.SaveVersionAsync(config, ct);
        if (!save.Saved)
            return BadRequest(save);

        return Ok(save);
    }

    public sealed class CompareVersionsRequest
    {
        public string LeftVersion { get; set; } = string.Empty;
        public string RightVersion { get; set; } = string.Empty;
    }
}