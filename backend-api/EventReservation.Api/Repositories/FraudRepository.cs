using EventReservation.Api.Data;
using EventReservation.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EventReservation.Api.Repositories;

public class FraudRepository : IFraudRepository
{
    private readonly AppDbContext _db;

    public FraudRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<int> CountRecentBookingsByUserAsync(int userId, DateTime sinceUtc) =>
        _db.Bookings.AsNoTracking().CountAsync(b => b.UserId == userId && b.CreatedAt >= sinceUtc);

    public Task<int> CountDistinctUsersByIpAsync(string ipAddress, DateTime sinceUtc) =>
        _db.BookingRiskAssessments.AsNoTracking()
            .Where(a => a.IpAddress == ipAddress && a.CreatedAt >= sinceUtc)
            .Select(a => a.UserId)
            .Distinct()
            .CountAsync();

    public Task<int> CountRecentNonAllowedAsync(int userId, string? ipAddress, DateTime sinceUtc) =>
        _db.BookingRiskAssessments.AsNoTracking()
            .Where(a => a.CreatedAt >= sinceUtc && a.Decision != RiskDecision.Allowed)
            .Where(a => a.UserId == userId || (ipAddress != null && a.IpAddress == ipAddress))
            .CountAsync();

    public async Task LogAsync(BookingRiskAssessment assessment)
    {
        _db.BookingRiskAssessments.Add(assessment);
        await _db.SaveChangesAsync();
    }

    public async Task<(int Total, List<BookingRiskAssessment> Items)> GetForOrganizerEventsAsync(int organizerUserId, int page, int pageSize)
    {
        var query = _db.BookingRiskAssessments.AsNoTracking()
            .Include(a => a.User)
            .Include(a => a.Event)
            .Where(a => a.Event!.CreatedByUserId == organizerUserId);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();
        return (total, items);
    }

    public async Task<(int Total, List<BookingRiskAssessment> Items)> GetPlatformWideAsync(int page, int pageSize)
    {
        var query = _db.BookingRiskAssessments.AsNoTracking().Include(a => a.User).Include(a => a.Event);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();
        return (total, items);
    }

    public Task<FraudSummaryCounts> GetSummaryForOrganizerAsync(int organizerUserId) =>
        SummaryAsync(_db.BookingRiskAssessments.AsNoTracking().Where(a => a.Event!.CreatedByUserId == organizerUserId));

    public Task<FraudSummaryCounts> GetSummaryPlatformWideAsync() =>
        SummaryAsync(_db.BookingRiskAssessments.AsNoTracking());

    private static async Task<FraudSummaryCounts> SummaryAsync(IQueryable<BookingRiskAssessment> query)
    {
        var since = DateTime.UtcNow.Date;
        var blockedToday = await query.CountAsync(a => a.CreatedAt >= since && a.Decision == RiskDecision.Blocked);
        var flaggedToday = await query.CountAsync(a => a.CreatedAt >= since && a.Decision == RiskDecision.Flagged);
        var total = await query.CountAsync();
        return new FraudSummaryCounts(blockedToday, flaggedToday, total);
    }
}
