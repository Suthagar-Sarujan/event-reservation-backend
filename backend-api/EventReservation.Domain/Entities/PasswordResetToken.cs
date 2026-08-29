namespace EventReservation.Domain.Entities;

/// <summary>
/// One row per issued password-reset link. Only a SHA-256 hash of the raw
/// token is ever stored - the raw token exists only in memory long enough to
/// email it once, so a database read alone can never be used to reset an
/// account's password. UsedAt is null until the token is consumed (or swept
/// as stale by a newer request/successful reset), at which point it can
/// never be used again regardless of ExpiresAt.
/// </summary>
public class PasswordResetToken
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UsedAt { get; set; }

    public User? User { get; set; }
}
