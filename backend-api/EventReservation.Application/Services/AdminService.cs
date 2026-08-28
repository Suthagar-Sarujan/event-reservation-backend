using EventReservation.Domain.Entities;
using EventReservation.Application.DTOs;
using EventReservation.Application.Repositories;

namespace EventReservation.Application.Services;

public class AdminService : IAdminService
{
    private readonly IUserRepository _users;
    private readonly IEventRepository _events;
    private readonly IBookingRepository _bookings;
    private readonly IRecommenderClient _recommender;
    private readonly IFraudRepository _fraud;

    public AdminService(IUserRepository users, IEventRepository events, IBookingRepository bookings, IRecommenderClient recommender, IFraudRepository fraud)
    {
        _users = users;
        _events = events;
        _bookings = bookings;
        _recommender = recommender;
        _fraud = fraud;
    }

    public async Task<AdminStatsDto> GetStatsAsync()
    {
        var totalUsers = await _users.CountAsync();
        var totalCustomers = await _users.CountByRoleAsync(UserRole.Customer);
        var totalOrganizers = await _users.CountByRoleAsync(UserRole.Organizer);
        var totalAdmins = await _users.CountByRoleAsync(UserRole.Admin);
        var totalEvents = await _events.CountAsync();
        var totalOrganizerEvents = await _events.CountOrganizerCreatedAsync();
        var totalBookings = await _bookings.CountAsync();
        var totalRevenue = await _bookings.SumConfirmedRevenueAsync();

        return new AdminStatsDto(
            totalUsers, totalCustomers, totalOrganizers, totalAdmins,
            totalEvents, totalEvents - totalOrganizerEvents, totalOrganizerEvents,
            totalBookings, totalRevenue);
    }

    public async Task<(int Total, int Page, int PageSize, List<AdminUserDto> Items)> GetUsersAsync(string? search, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (total, users) = await _users.SearchAsync(search, page, pageSize);
        var items = users.Select(u => new AdminUserDto(u.UserId, u.FullName, u.Email, u.Role.ToString(), u.CreatedAt)).ToList();
        return (total, page, pageSize, items);
    }

    public async Task<AdminRoleUpdateStatus> UpdateUserRoleAsync(int userId, int currentUserId, string role)
    {
        if (!Enum.TryParse<UserRole>(role, ignoreCase: true, out var newRole))
        {
            return AdminRoleUpdateStatus.InvalidRole;
        }
        if (userId == currentUserId && newRole != UserRole.Admin)
        {
            return AdminRoleUpdateStatus.CantRemoveOwnAdmin;
        }

        var user = await _users.GetByIdAsync(userId);
        if (user is null) return AdminRoleUpdateStatus.UserNotFound;

        user.Role = newRole;
        await _users.SaveChangesAsync();
        return AdminRoleUpdateStatus.Success;
    }

    public async Task<(int Total, int Page, int PageSize, List<AdminEventDto> Items)> GetEventsAsync(string? search, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (total, events) = await _events.AdminSearchAsync(search, page, pageSize);

        var creatorIds = events.Where(e => e.CreatedByUserId != null).Select(e => e.CreatedByUserId!.Value).Distinct().ToList();
        var creators = await _users.GetEmailsByIdsAsync(creatorIds);

        var items = events.Select(e => new AdminEventDto(
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

        return (total, page, pageSize, items);
    }

    public async Task<bool> CancelEventAsync(long id)
    {
        var e = await _events.GetForAdminUpdateAsync(id);
        if (e is null) return false;

        e.Status = "cancelled";
        await _events.SaveChangesAsync();
        await _recommender.RefreshAsync();
        return true;
    }

    public async Task<AdminEventUpdateStatus> UpdateEventAsync(long id, UpdateEventRequest request)
    {
        var e = await _events.GetForAdminUpdateAsync(id);
        if (e is null) return AdminEventUpdateStatus.NotFound;

        if (request.Status is not ("normal" or "cancelled"))
        {
            return AdminEventUpdateStatus.InvalidStatus;
        }

        e.Name = request.Name;
        e.DatetimeUtc = request.DatetimeUtc;
        e.Status = request.Status;
        e.ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim();
        await _events.SaveChangesAsync();
        await _recommender.RefreshAsync();

        return AdminEventUpdateStatus.Success;
    }

    public async Task<(int Total, int Page, int PageSize, List<AdminBookingDto> Items)> GetBookingsAsync(string? search, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (total, bookings) = await _bookings.AdminSearchAsync(search, page, pageSize);
        var items = bookings.Select(b => new AdminBookingDto(
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
        )).ToList();

        return (total, page, pageSize, items);
    }

    public async Task<List<TrendPointDto>> GetBookingTrendAsync(int days)
    {
        days = Math.Clamp(days, 1, 90);
        var points = await _bookings.GetDailyTrendAsync(days);
        return points.Select(p => new TrendPointDto(p.Date, p.Bookings, p.Revenue)).ToList();
    }

    public async Task<FraudOverviewDto> GetFraudOverviewAsync()
    {
        var summary = await _fraud.GetSummaryPlatformWideAsync();
        var (_, items) = await _fraud.GetPlatformWideAsync(1, 50);
        return new FraudOverviewDto(summary.BlockedToday, summary.FlaggedToday, summary.TotalAssessed, items.Select(ToRiskDto).ToList());
    }

    private static RiskAssessmentDto ToRiskDto(BookingRiskAssessment a) => new(
        a.BookingRiskId,
        a.UserId,
        a.User?.Email ?? "",
        a.EventId,
        a.Event?.Name ?? "",
        a.BookingId,
        a.IpAddress,
        a.RequestedQuantity,
        a.RiskScore,
        a.RiskLevel.ToString(),
        a.Decision.ToString(),
        a.Reasons,
        a.CreatedAt
    );
}
