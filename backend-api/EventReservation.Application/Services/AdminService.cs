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
    private readonly IGateRepository _gates;
    private readonly IGateScanRepository _gateScans;
    private readonly IEmailService _email;

    public AdminService(
        IUserRepository users,
        IEventRepository events,
        IBookingRepository bookings,
        IRecommenderClient recommender,
        IFraudRepository fraud,
        IGateRepository gates,
        IGateScanRepository gateScans,
        IEmailService email)
    {
        _users = users;
        _events = events;
        _bookings = bookings;
        _recommender = recommender;
        _fraud = fraud;
        _gates = gates;
        _gateScans = gateScans;
        _email = email;
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
            b.CreatedAt,
            b.EmailStatus.ToString(),
            b.EmailSentAt
        )).ToList();

        return (total, page, pageSize, items);
    }

    // Admin has no ownership restriction - can resend for any booking platform-wide.
    public Task<EmailSendResult> ResendBookingEmailAsync(int bookingId) =>
        _email.SendBookingConfirmationAsync(bookingId);

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

    public async Task<(int Total, int Page, int PageSize, List<GateDto> Items)> GetGatesAsync(string? search, string? status, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        GateStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<GateStatus>(status, ignoreCase: true, out var s))
        {
            parsedStatus = s;
        }

        var (total, gates) = await _gates.SearchAsync(search, parsedStatus, page, pageSize);
        var items = gates.Select(ToGateDto).ToList();
        return (total, page, pageSize, items);
    }

    public async Task<GateDetailDto?> GetGateDetailAsync(int gateId)
    {
        var gate = await _gates.GetDetailAsync(gateId);
        if (gate is null) return null;

        var assignedUsers = new List<GateUserSummaryDto>();
        foreach (var a in gate.Assignments.Where(a => a.User is not null))
        {
            var allGateIds = await _gates.GetAssignedGateIdsForUserAsync(a.UserId);
            assignedUsers.Add(new GateUserSummaryDto(a.User!.UserId, a.User.FullName, a.User.Email, allGateIds));
        }

        return new GateDetailDto(gate.GateId, gate.Name, gate.Description, gate.Status.ToString(), gate.CreatedAt, gate.UpdatedAt, assignedUsers);
    }

    public async Task<(GateCreationStatus Status, GateDto? Gate)> CreateGateAsync(string name, string? description, int adminUserId)
    {
        var trimmedName = name.Trim();
        if (await _gates.NameExistsAsync(trimmedName))
        {
            return (GateCreationStatus.DuplicateName, null);
        }

        var now = DateTime.UtcNow;
        var gate = new Gate
        {
            Name = trimmedName,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Status = GateStatus.Active,
            CreatedByUserId = adminUserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await _gates.AddAsync(gate);

        return (GateCreationStatus.Success, ToGateDto(gate));
    }

    public async Task<GateUpdateStatus> UpdateGateAsync(int gateId, string name, string? description)
    {
        var gate = await _gates.GetByIdAsync(gateId);
        if (gate is null) return GateUpdateStatus.NotFound;

        var trimmedName = name.Trim();
        if (await _gates.NameExistsAsync(trimmedName, excludeGateId: gateId))
        {
            return GateUpdateStatus.DuplicateName;
        }

        gate.Name = trimmedName;
        gate.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        gate.UpdatedAt = DateTime.UtcNow;
        await _gates.SaveChangesAsync();

        return GateUpdateStatus.Success;
    }

    public async Task<GateStatusChangeStatus> SetGateStatusAsync(int gateId, bool active)
    {
        var gate = await _gates.GetByIdAsync(gateId);
        if (gate is null) return GateStatusChangeStatus.NotFound;

        gate.Status = active ? GateStatus.Active : GateStatus.Inactive;
        gate.UpdatedAt = DateTime.UtcNow;
        await _gates.SaveChangesAsync();

        return GateStatusChangeStatus.Success;
    }

    public Task<GateDeleteStatus> DeleteGateAsync(int gateId) => _gates.DeleteAsync(gateId);

    public async Task<(GateUserCreationStatus Status, GateUserSummaryDto? User)> CreateGateUserAsync(string fullName, string email, string password, List<int> gateIds)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (await _users.EmailExistsAsync(normalizedEmail))
        {
            return (GateUserCreationStatus.EmailAlreadyExists, null);
        }

        var ids = gateIds.Distinct().ToList();
        foreach (var gateId in ids)
        {
            if (await _gates.GetByIdAsync(gateId) is null)
            {
                return (GateUserCreationStatus.GateNotFound, null);
            }
        }

        var user = new User
        {
            FullName = fullName.Trim(),
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = UserRole.GateUser,
            CreatedAt = DateTime.UtcNow,
        };
        await _users.AddAsync(user);

        foreach (var gateId in ids)
        {
            await _gates.AssignUserAsync(gateId, user.UserId, assignedByUserId: null);
        }

        return (GateUserCreationStatus.Success, new GateUserSummaryDto(user.UserId, user.FullName, user.Email, ids));
    }

    public async Task<(int Total, int Page, int PageSize, List<GateUserSummaryDto> Items)> GetGateUsersAsync(string? search, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (total, users, gateIdsByUser) = await _gates.SearchGateUsersAsync(search, page, pageSize);
        var items = users.Select(u => new GateUserSummaryDto(
            u.UserId, u.FullName, u.Email,
            gateIdsByUser.TryGetValue(u.UserId, out var ids) ? ids : [])).ToList();

        return (total, page, pageSize, items);
    }

    public async Task<GateUserAssignStatus> AssignGateUserAsync(int gateId, int userId, int assignedByUserId)
    {
        if (await _gates.GetByIdAsync(gateId) is null) return GateUserAssignStatus.GateNotFound;

        var user = await _users.GetByIdAsync(userId);
        if (user is null) return GateUserAssignStatus.UserNotFound;
        if (user.Role != UserRole.GateUser) return GateUserAssignStatus.UserNotGateRole;

        await _gates.AssignUserAsync(gateId, userId, assignedByUserId);
        return GateUserAssignStatus.Success;
    }

    public async Task<GateUserRemoveStatus> RemoveGateUserAsync(int gateId, int userId)
    {
        var removed = await _gates.RemoveUserAsync(gateId, userId);
        return removed ? GateUserRemoveStatus.Success : GateUserRemoveStatus.NotFound;
    }

    public async Task<(int Total, int Page, int PageSize, List<GateScanHistoryDto> Items)> GetGateScanHistoryAsync(int? gateId, string? status, DateTime? fromUtc, DateTime? toUtc, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        GateScanStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<GateScanStatus>(status, ignoreCase: true, out var s))
        {
            parsedStatus = s;
        }

        var (total, scans) = await _gateScans.SearchAsync(gateId, parsedStatus, fromUtc, toUtc, page, pageSize);
        var items = scans.Select(ToGateScanHistoryDto).ToList();
        return (total, page, pageSize, items);
    }

    private static GateDto ToGateDto(Gate g) => new(
        g.GateId, g.Name, g.Description, g.Status.ToString(), g.Assignments.Count, g.CreatedAt, g.UpdatedAt);

    private static GateScanHistoryDto ToGateScanHistoryDto(GateScanHistory s) => new(
        s.ScanId,
        s.GateId,
        s.Gate?.Name ?? "Unknown gate",
        s.ScannedByUserId,
        s.ScannedByUser?.FullName ?? "",
        s.BookingId,
        s.Booking?.BookingReference,
        s.Event?.Name,
        s.ScanType.ToString(),
        s.Status.ToString(),
        s.FailureReason,
        s.ScannedAt
    );
}
