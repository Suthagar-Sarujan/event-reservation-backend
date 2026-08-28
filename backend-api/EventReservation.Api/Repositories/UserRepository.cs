using EventReservation.Api.Data;
using EventReservation.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EventReservation.Api.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;

    public UserRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<User?> GetByEmailAsync(string email) =>
        _db.Users.FirstOrDefaultAsync(u => u.Email == email);

    public Task<User?> GetByIdAsync(int id) =>
        _db.Users.FindAsync(id).AsTask();

    public Task<bool> EmailExistsAsync(string email) =>
        _db.Users.AnyAsync(u => u.Email == email);

    public async Task AddAsync(User user)
    {
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
    }

    public async Task<(int Total, List<User> Items)> SearchAsync(string? search, int page, int pageSize)
    {
        var query = _db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u => u.FullName.Contains(search) || u.Email.Contains(search));
        }
        query = query.OrderByDescending(u => u.CreatedAt);

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (total, items);
    }

    public Task<Dictionary<int, string>> GetEmailsByIdsAsync(IEnumerable<int> userIds) =>
        _db.Users.AsNoTracking().Where(u => userIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.Email);

    public Task<int> CountAsync() => _db.Users.CountAsync();

    public Task<int> CountByRoleAsync(UserRole role) => _db.Users.CountAsync(u => u.Role == role);

    public Task SaveChangesAsync() => _db.SaveChangesAsync();
}
