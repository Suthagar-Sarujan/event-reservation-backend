using EventReservation.Application.Repositories;
using EventReservation.Infrastructure.Persistence;
using EventReservation.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EventReservation.Infrastructure.Persistence.Repositories;

public class PasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly AppDbContext _db;

    public PasswordResetTokenRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(PasswordResetToken token)
    {
        _db.PasswordResetTokens.Add(token);
        await _db.SaveChangesAsync();
    }

    public Task<PasswordResetToken?> GetByTokenHashAsync(string tokenHash) =>
        _db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

    public async Task InvalidateActiveTokensForUserAsync(int userId)
    {
        var now = DateTime.UtcNow;
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE password_reset_tokens SET used_at = {now} WHERE user_id = {userId} AND used_at IS NULL AND expires_at > {now}");
    }
}
