using Helrift.Gate.Api.Services.Escrow;
using Helrift.Gate.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Helrift.Gate.Api.Controllers;

[ApiController]
[Route("api/v1/escrow/bridge")]
[Authorize(Policy = "ServerOnly")]
public sealed class EscrowBridgeController(IGameServerEscrowBridge bridge) : ControllerBase
{
    [HttpPost("complete")]
    public IActionResult Complete([FromBody] GameServerEscrowOperationCompleteRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.OperationId))
            return BadRequest("operationId is required.");

        var matched = bridge.Complete(request);
        return matched ? Ok() : NotFound("No pending bridge operation.");
    }
}