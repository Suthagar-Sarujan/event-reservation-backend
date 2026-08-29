using EventReservation.Domain.Entities;

namespace EventReservation.Application.Repositories;

public interface IPasswordResetTokenRepository
{
    Task AddAsync(PasswordResetToken token);

    /// <summary>Tracked lookup by the token's SHA-256 hash - the only way a reset token is ever found.</summary>
    Task<PasswordResetToken?> GetByTokenHashAsync(string tokenHash);

    /// <summary>
    /// Marks every still-active (unused, unexpired) token for this user as
    /// used, atomically. Called both before issuing a new token (a fresh
    /// request supersedes any still-live older link) and after a successful
    /// reset (the just-used token, and any other stray active ones, must
    /// never be usable again).
    /// </summary>
    Task InvalidateActiveTokensForUserAsync(int userId);
}
