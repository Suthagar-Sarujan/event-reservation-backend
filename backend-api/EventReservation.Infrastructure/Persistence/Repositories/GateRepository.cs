using EventReservation.Application.Repositories;
using EventReservation.Infrastructure.Persistence;
using EventReservation.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EventReservation.Infrastructure.Persistence.Repositories;

public class GateRepository : IGateRepository
{
    private readonly AppDbContext _db;

    public GateRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<(int Total, List<Gate> Items)> SearchAsync(string? search, GateStatus? status, int page, int pageSize)
    {
        var query = _db.Gates.AsNoTracking().Include(g => g.Assignments).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(g => g.Name.Contains(search));
        }
        if (status is not null)
        {
            query = query.Where(g => g.Status == status);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(g => g.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();
        return (total, items);
    }

    public Task<Gate?> GetByIdAsync(int gateId) =>
        _db.Gates.FirstOrDefaultAsync(g => g.GateId == gateId);

    public Task<Gate?> GetDetailAsync(int gateId) =>
        _db.Gates.AsNoTracking()
            .Include(g => g.Assignments).ThenInclude(a => a.User)
            .FirstOrDefaultAsync(g => g.GateId == gateId);

    public Task<bool> NameExistsAsync(string name, int? excludeGateId = null)
    {
        var query = _db.Gates.AsNoTracking().Where(g => g.Name == name);
        if (excludeGateId is not null)
        {
            query = query.Where(g => g.GateId != excludeGateId.Value);
        }
        return query.AnyAsync();
    }

    public async Task AddAsync(Gate gate)
    {
        _db.Gates.Add(gate);
        await _db.SaveChangesAsync();
    }

    public Task SaveChangesAsync() => _db.SaveChangesAsync();

    public async Task<GateDeleteStatus> DeleteAsync(int gateId)
    {
        var gate = await _db.Gates.FirstOrDefaultAsync(g => g.GateId == gateId);
        if (gate is null) return GateDeleteStatus.NotFound;

        var hasAssignments = await _db.GateUserAssignments.AnyAsync(a => a.GateId == gateId);
        var hasHistory = await _db.GateScanHistories.AnyAsync(s => s.GateId == gateId);
        if (hasAssignments || hasHistory)
        {
            return GateDeleteStatus.HasHistory;
        }

        _db.Gates.Remove(gate);
        await _db.SaveChangesAsync();
        return GateDeleteStatus.Success;
    }

    public async Task AssignUserAsync(int gateId, int userId, int? assignedByUserId)
    {
        var exists = await _db.GateUserAssignments.AnyAsync(a => a.GateId == gateId && a.UserId == userId);
        if (exists) return;

        _db.GateUserAssignments.Add(new GateUserAssignment
        {
            GateId = gateId,
            UserId = userId,
            AssignedAt = DateTime.UtcNow,
            AssignedByUserId = assignedByUserId,
        });
        await _db.SaveChangesAsync();
    }

    public async Task<bool> RemoveUserAsync(int gateId, int userId)
    {
        var assignment = await _db.GateUserAssignments.FirstOrDefaultAsync(a => a.GateId == gateId && a.UserId == userId);
        if (assignment is null) return false;

        _db.GateUserAssignments.Remove(assignment);
        await _db.SaveChangesAsync();
        return true;
    }

    public Task<List<int>> GetAssignedGateIdsForUserAsync(int userId) =>
        _db.GateUserAssignments.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => a.GateId)
            .ToListAsync();

    public Task<List<Gate>> GetAssignedActiveGatesForUserAsync(int userId) =>
        _db.Gates.AsNoTracking()
            .Where(g => g.Status == GateStatus.Active && g.Assignments.Any(a => a.UserId == userId))
            .Include(g => g.Assignments)
            .OrderBy(g => g.Name)
            .ToListAsync();

    public Task<bool> IsUserAssignedToGateAsync(int userId, int gateId) =>
        _db.GateUserAssignments.AsNoTracking().AnyAsync(a => a.UserId == userId && a.GateId == gateId);

    public async Task<(int Total, List<User> Items, Dictionary<int, List<int>> GateIdsByUser)> SearchGateUsersAsync(string? search, int page, int pageSize)
    {
        var query = _db.Users.AsNoTracking().Where(u => u.Role == UserRole.GateUser).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u => u.FullName.Contains(search) || u.Email.Contains(search));
        }
        query = query.OrderByDescending(u => u.CreatedAt);

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var userIds = items.Select(u => u.UserId).ToList();
        var assignments = await _db.GateUserAssignments.AsNoTracking()
            .Where(a => userIds.Contains(a.UserId))
            .Select(a => new { a.UserId, a.GateId })
            .ToListAsync();
        var gateIdsByUser = assignments
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.GateId).ToList());

        return (total, items, gateIdsByUser);
    }
}
