using System.IdentityModel.Tokens.Jwt;
using EventReservation.Api.Data;
using EventReservation.Api.Data.Entities;
using EventReservation.Api.DTOs;
using EventReservation.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventReservation.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly RecommenderClient _recommender;

    public AdminController(AppDbContext db, RecommenderClient recommender)
    {
        _db = db;
        _recommender = recommender;
    }

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet("stats")]
    public async Task<ActionResult<AdminStatsDto>> GetStats()
    {
        var totalUsers = await _db.Users.CountAsync();
        var totalCustomers = await _db.Users.CountAsync(u => u.Role == UserRole.Customer);
        var totalOrganizers = await _db.Users.CountAsync(u => u.Role == UserRole.Organizer);
        var totalAdmins = await _db.Users.CountAsync(u => u.Role == UserRole.Admin);
        var totalEvents = await _db.Events.CountAsync();
        var totalOrganizerEvents = await _db.Events.CountAsync(e => e.CreatedByUserId != null);
        var totalBookings = await _db.Bookings.CountAsync();
        var totalRevenue = await _db.Bookings.Where(b => b.Status == BookingStatus.Confirmed).SumAsync(b => (decimal?)b.TotalAmount) ?? 0m;

        return Ok(new AdminStatsDto(
            totalUsers, totalCustomers, totalOrganizers, totalAdmins,
            totalEvents, totalEvents - totalOrganizerEvents, totalOrganizerEvents,
            totalBookings, totalRevenue));
    }

    [HttpGet("users")]
    public async Task<ActionResult> GetUsers([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u => u.FullName.Contains(search) || u.Email.Contains(search));
        }
        query = query.OrderByDescending(u => u.CreatedAt);

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new AdminUserDto(u.UserId, u.FullName, u.Email, u.Role.ToString(), u.CreatedAt))
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpPatch("users/{id:int}/role")]
    public async Task<ActionResult> UpdateUserRole(int id, UpdateUserRoleRequest request)
    {
        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var newRole))
        {
            return BadRequest(new { message = "Role must be 'customer', 'organizer', or 'admin'." });
        }
        if (id == CurrentUserId && newRole != UserRole.Admin)
        {
            return BadRequest(new { message = "You can't remove your own admin access." });
        }

        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();

        user.Role = newRole;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("events")]
    public async Task<ActionResult> GetEvents([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Events.AsNoTracking().Include(e => e.Venue).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(e => e.Name.Contains(search));
        }
        query = query.OrderByDescending(e => e.CreatedByUserId != null).ThenByDescending(e => e.DatetimeUtc);

        var total = await query.CountAsync();
        var pagedEvents = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Include(e => e.Listings)
            .ToListAsync();

        var creatorIds = pagedEvents.Where(e => e.CreatedByUserId != null).Select(e => e.CreatedByUserId!.Value).Distinct().ToList();
        var creators = await _db.Users.AsNoTracking().Where(u => creatorIds.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, u => u.Email);

        var items = pagedEvents.Select(e => new AdminEventDto(
            e.EventId,
            e.Name,
            e.DatetimeUtc,
            e.Venue!.Name,
            e.Status ?? "normal",
            e.CreatedByUserId != null ? "organizer" : "seatgeek",
            e.CreatedByUserId != null && creators.TryGetValue(e.CreatedByUserId.Value, out var email) ? email : null,
            e.Listings.Sum(l => l.Quantity - l.QuantityRemaining),
            e.Listings.Sum(l => (l.Quantity - l.QuantityRemaining) * l.UnitPrice),
            e.ImageUrl
        )).ToList();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpPost("events/{id:long}/cancel")]
    public async Task<ActionResult> CancelEvent(long id)
    {
        var e = await _db.Events.FirstOrDefaultAsync(ev => ev.EventId == id);
        if (e is null) return NotFound();

        e.Status = "cancelled";
        await _db.SaveChangesAsync();
        await _recommender.RefreshAsync();
        return NoContent();
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
        var e = await _db.Events.FirstOrDefaultAsync(ev => ev.EventId == id);
        if (e is null) return NotFound();

        if (request.Status is not ("normal" or "cancelled"))
        {
            return BadRequest(new { message = "Status must be 'normal' or 'cancelled'." });
        }

        e.Name = request.Name;
        e.DatetimeUtc = request.DatetimeUtc;
        e.Status = request.Status;
        e.ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim();
        await _db.SaveChangesAsync();
        await _recommender.RefreshAsync();

        return NoContent();
    }

    [HttpGet("bookings")]
    public async Task<ActionResult> GetBookings([FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Bookings.AsNoTracking().Include(b => b.User).Include(b => b.Event).Include(b => b.Items)
            .OrderByDescending(b => b.CreatedAt);

        var total = await _db.Bookings.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(b => new AdminBookingDto(
                b.BookingId,
                b.BookingReference,
                b.User!.FullName,
                b.User.Email,
                b.EventId,
                b.Event!.Name,
                b.Items.Sum(i => i.Quantity),
                b.TotalAmount,
                b.Status.ToString(),
                b.CreatedAt
            ))
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }
}
