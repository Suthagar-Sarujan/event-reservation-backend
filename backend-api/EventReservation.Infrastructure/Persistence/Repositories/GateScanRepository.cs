using EventReservation.Application.Repositories;
using EventReservation.Infrastructure.Persistence;
using EventReservation.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EventReservation.Infrastructure.Persistence.Repositories;

public class GateScanRepository : IGateScanRepository
{
    private readonly AppDbContext _db;

    public GateScanRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(GateScanHistory scan)
    {
        _db.GateScanHistories.Add(scan);
        await _db.SaveChangesAsync();
    }

    public async Task<(int Total, List<GateScanHistory> Items)> SearchAsync(int? gateId, GateScanStatus? status, DateTime? fromUtc, DateTime? toUtc, int page, int pageSize)
    {
        var query = _db.GateScanHistories.AsNoTracking()
            .Include(s => s.Gate)
            .Include(s => s.ScannedByUser)
            .Include(s => s.Booking)
            .Include(s => s.Event)
            .AsQueryable();

        if (gateId is not null)
        {
            query = query.Where(s => s.GateId == gateId);
        }
        if (status is not null)
        {
            query = query.Where(s => s.Status == status);
        }
        if (fromUtc is not null)
        {
            query = query.Where(s => s.ScannedAt >= fromUtc);
        }
        if (toUtc is not null)
        {
            query = query.Where(s => s.ScannedAt <= toUtc);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(s => s.ScannedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();
        return (total, items);
    }
}
