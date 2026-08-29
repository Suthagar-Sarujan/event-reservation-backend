using System.IdentityModel.Tokens.Jwt;
using EventReservation.Application.DTOs;
using EventReservation.Application.Services;
using EventReservation.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventReservation.Api.Controllers;

/// <summary>
/// Gate User-facing runtime scanning flow: which gates this staff account is
/// assigned to, and scanning a ticket at one of them. Distinct from
/// TicketVerificationController's Organizer/Admin generic verify flow -
/// this one is gate-scoped and permission-checked server-side.
/// </summary>
[ApiController]
[Route("api/gate")]
[Authorize(Roles = "GateUser")]
public class GateScanController : ControllerBase
{
    private readonly IGateService _gateService;

    public GateScanController(IGateService gateService)
    {
        _gateService = gateService;
    }

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet("my-gates")]
    public async Task<ActionResult<List<GateDto>>> GetMyGates()
    {
        var gates = await _gateService.GetMyGatesAsync(CurrentUserId);
        return Ok(gates);
    }

    // Always 200, even when the scan is rejected (success: false + message) -
    // matches the existing convention in TicketVerificationController, which
    // also returns 200 with a Found:false/message rather than a 4xx for an
    // expected business-rule rejection. The frontend only branches on the
    // `success` field, never on HTTP status.
    [HttpPost("scan")]
    public async Task<ActionResult<GateScanResultDto>> Scan(GateScanRequest request)
    {
        // A bad ScanType value is a malformed request, not a scan outcome -
        // unlike every rejection inside ScanTicketAsync, this is the one case
        // that gets a real 4xx rather than a 200 success:false.
        if (!Enum.TryParse<GateScanType>(request.ScanType, ignoreCase: true, out var scanType))
        {
            return BadRequest(new { message = "Scan type must be 'CheckIn' or 'CheckOut'." });
        }

        var result = await _gateService.ScanTicketAsync(CurrentUserId, request.GateId, request.Code, request.EventId, scanType);
        return Ok(result);
    }
}
