using EventReservation.Application.Repositories;
using EventReservation.Infrastructure.Persistence;
using EventReservation.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EventReservation.Infrastructure.Persistence.Repositories;

public class UserPreferenceRepository : IUserPreferenceRepository
{
    private readonly AppDbContext _db;

    public UserPreferenceRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<UserPreference?> GetByUserIdAsync(int userId) =>
        _db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);

    public Task<bool> ExistsAsync(int userId) =>
        _db.UserPreferences.AnyAsync(p => p.UserId == userId);

    public async Task UpsertAsync(UserPreference preference)
    {
        var existing = await _db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == preference.UserId);
        if (existing is null)
        {
            preference.CreatedAt = DateTime.UtcNow;
            preference.UpdatedAt = preference.CreatedAt;
            _db.UserPreferences.Add(preference);
        }
        else
        {
            existing.EventTypes = preference.EventTypes;
            existing.MusicGenres = preference.MusicGenres;
            existing.Atmosphere = preference.Atmosphere;
            existing.AttendanceFrequency = preference.AttendanceFrequency;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }
}
