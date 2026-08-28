using System.IdentityModel.Tokens.Jwt;
using EventReservation.Application.DTOs;
using EventReservation.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventReservation.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;

    public AdminController(IAdminService adminService)
    {
        _adminService = adminService;
    }

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet("stats")]
    public async Task<ActionResult<AdminStatsDto>> GetStats()
    {
        var stats = await _adminService.GetStatsAsync();
        return Ok(stats);
    }

    [HttpGet("users")]
    public async Task<ActionResult> GetUsers([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var (total, resolvedPage, resolvedPageSize, items) = await _adminService.GetUsersAsync(search, page, pageSize);
        return Ok(new { total, page = resolvedPage, pageSize = resolvedPageSize, items });
    }

    [HttpPatch("users/{id:int}/role")]
    public async Task<ActionResult> UpdateUserRole(int id, UpdateUserRoleRequest request)
    {
        var status = await _adminService.UpdateUserRoleAsync(id, CurrentUserId, request.Role);
        return status switch
        {
            AdminRoleUpdateStatus.InvalidRole => BadRequest(new { message = "Role must be 'customer', 'organizer', or 'admin'." }),
            AdminRoleUpdateStatus.CantRemoveOwnAdmin => BadRequest(new { message = "You can't remove your own admin access." }),
            AdminRoleUpdateStatus.UserNotFound => NotFound(),
            _ => NoContent(),
        };
    }

    [HttpGet("events")]
    public async Task<ActionResult> GetEvents([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var (total, resolvedPage, resolvedPageSize, items) = await _adminService.GetEventsAsync(search, page, pageSize);
        return Ok(new { total, page = resolvedPage, pageSize = resolvedPageSize, items });
    }

    [HttpPost("events/{id:long}/cancel")]
    public async Task<ActionResult> CancelEvent(long id)
    {
        var found = await _adminService.CancelEventAsync(id);
        return found ? NoContent() : NotFound();
    }

    /// <summary>
    /// Admin can edit core details on ANY event - imported SeatGeek events
    /// included, not just organizer-created ones (unlike OrganizerController's
    /// UpdateEvent, which is scoped to events the organizer owns). Deliberately
    /// limited to the same core fields organizers can edit (name/date/status/
    /// image) - venue, performers, and SeatGeek listing/pricing data are left
    /// alone.
    /// </summary>
    [HttpPut("events/{id:long}")]
    public async Task<ActionResult> UpdateEvent(long id, UpdateEventRequest request)
    {
        var status = await _adminService.UpdateEventAsync(id, request);
        return status switch
        {
            AdminEventUpdateStatus.NotFound => NotFound(),
            AdminEventUpdateStatus.InvalidStatus => BadRequest(new { message = "Status must be 'normal' or 'cancelled'." }),
            _ => NoContent(),
        };
    }

    [HttpGet("bookings")]
    public async Task<ActionResult> GetBookings([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var (total, resolvedPage, resolvedPageSize, items) = await _adminService.GetBookingsAsync(search, page, pageSize);
        return Ok(new { total, page = resolvedPage, pageSize = resolvedPageSize, items });
    }

    [HttpGet("stats/trend")]
    public async Task<ActionResult<List<TrendPointDto>>> GetBookingTrend([FromQuery] int days = 30)
    {
        var trend = await _adminService.GetBookingTrendAsync(days);
        return Ok(trend);
    }

    [HttpGet("fraud-overview")]
    public async Task<ActionResult<FraudOverviewDto>> GetFraudOverview()
    {
        var overview = await _adminService.GetFraudOverviewAsync();
        return Ok(overview);
    }
}
