using System.IdentityModel.Tokens.Jwt;
using EventReservation.Application.DTOs;
using EventReservation.Application.Repositories;
using EventReservation.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventReservation.Api.Controllers;

/// <summary>
/// Admin-facing gate management: creating/editing/deactivating physical
/// gates, creating Gate User staff accounts, assigning them to gates, and
/// reviewing the scan audit trail. The Gate User's own runtime scanning flow
/// is a separate, GateUser-scoped controller (see GateScanController).
/// </summary>
[ApiController]
[Route("api/admin/gates")]
[Authorize(Roles = "Admin")]
public class GatesController : ControllerBase
{
    private readonly IAdminService _adminService;

    public GatesController(IAdminService adminService)
    {
        _adminService = adminService;
    }

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet]
    public async Task<ActionResult> GetGates([FromQuery] string? search, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var (total, resolvedPage, resolvedPageSize, items) = await _adminService.GetGatesAsync(search, status, page, pageSize);
        return Ok(new { total, page = resolvedPage, pageSize = resolvedPageSize, items });
    }

    // Static segment routes ("users", "scan-history") must be registered
    // before the "{id:int}" route below so ASP.NET's routing doesn't try to
    // match them against the int constraint first.
    [HttpGet("users")]
    public async Task<ActionResult> GetGateUsers([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var (total, resolvedPage, resolvedPageSize, items) = await _adminService.GetGateUsersAsync(search, page, pageSize);
        return Ok(new { total, page = resolvedPage, pageSize = resolvedPageSize, items });
    }

    [HttpPost("users")]
    public async Task<ActionResult<GateUserSummaryDto>> CreateGateUser(CreateGateUserRequest request)
    {
        var (status, user) = await _adminService.CreateGateUserAsync(request.FullName, request.Email, request.Password, request.GateIds ?? []);
        return status switch
        {
            GateUserCreationStatus.EmailAlreadyExists => BadRequest(new { message = "Email already registered." }),
            GateUserCreationStatus.GateNotFound => BadRequest(new { message = "One or more selected gates do not exist." }),
            _ => StatusCode(StatusCodes.Status201Created, user),
        };
    }

    [HttpGet("scan-history")]
    public async Task<ActionResult> GetScanHistory([FromQuery] int? gateId, [FromQuery] string? status, [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var (total, resolvedPage, resolvedPageSize, items) = await _adminService.GetGateScanHistoryAsync(gateId, status, fromUtc, toUtc, page, pageSize);
        return Ok(new { total, page = resolvedPage, pageSize = resolvedPageSize, items });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<GateDetailDto>> GetGateDetail(int id)
    {
        var gate = await _adminService.GetGateDetailAsync(id);
        return gate is null ? NotFound() : Ok(gate);
    }

    [HttpPost]
    public async Task<ActionResult<GateDto>> CreateGate(CreateGateRequest request)
    {
        var (status, gate) = await _adminService.CreateGateAsync(request.Name, request.Description, CurrentUserId);
        return status switch
        {
            GateCreationStatus.DuplicateName => BadRequest(new { message = "A gate with this name already exists." }),
            _ => CreatedAtAction(nameof(GetGateDetail), new { id = gate!.GateId }, gate),
        };
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult> UpdateGate(int id, UpdateGateRequest request)
    {
        var status = await _adminService.UpdateGateAsync(id, request.Name, request.Description);
        return status switch
        {
            GateUpdateStatus.NotFound => NotFound(),
            GateUpdateStatus.DuplicateName => BadRequest(new { message = "A gate with this name already exists." }),
            _ => NoContent(),
        };
    }

    [HttpPost("{id:int}/activate")]
    public async Task<ActionResult> ActivateGate(int id)
    {
        var status = await _adminService.SetGateStatusAsync(id, active: true);
        return status == GateStatusChangeStatus.NotFound ? NotFound() : NoContent();
    }

    [HttpPost("{id:int}/deactivate")]
    public async Task<ActionResult> DeactivateGate(int id)
    {
        var status = await _adminService.SetGateStatusAsync(id, active: false);
        return status == GateStatusChangeStatus.NotFound ? NotFound() : NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteGate(int id)
    {
        var status = await _adminService.DeleteGateAsync(id);
        return status switch
        {
            GateDeleteStatus.NotFound => NotFound(),
            GateDeleteStatus.HasHistory => Conflict(new { message = "This gate has scan history and cannot be deleted. Deactivate it instead." }),
            _ => NoContent(),
        };
    }

    [HttpPost("{gateId:int}/users")]
    public async Task<ActionResult> AssignGateUser(int gateId, AssignGateUserRequest request)
    {
        var status = await _adminService.AssignGateUserAsync(gateId, request.UserId, CurrentUserId);
        return status switch
        {
            GateUserAssignStatus.GateNotFound => NotFound(),
            GateUserAssignStatus.UserNotFound => NotFound(),
            GateUserAssignStatus.UserNotGateRole => BadRequest(new { message = "This user is not a Gate User." }),
            _ => NoContent(),
        };
    }

    [HttpDelete("{gateId:int}/users/{userId:int}")]
    public async Task<ActionResult> RemoveGateUser(int gateId, int userId)
    {
        var status = await _adminService.RemoveGateUserAsync(gateId, userId);
        return status == GateUserRemoveStatus.NotFound ? NotFound() : NoContent();
    }
}
