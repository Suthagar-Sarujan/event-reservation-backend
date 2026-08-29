using System.Security.Cryptography;
using EventReservation.Domain.Entities;
using EventReservation.Application.DTOs;
using EventReservation.Application.Repositories;

namespace EventReservation.Application.Services;

public class AuthService : IAuthService
{
    private const int ResetTokenExpiryMinutes = 15;

    private readonly IUserRepository _users;
    private readonly IUserPreferenceRepository _preferences;
    private readonly IPasswordResetTokenRepository _resetTokens;
    private readonly IEmailService _email;
    private readonly IJwtTokenService _jwt;

    public AuthService(
        IUserRepository users,
        IUserPreferenceRepository preferences,
        IPasswordResetTokenRepository resetTokens,
        IEmailService email,
        IJwtTokenService jwt)
    {
        _users = users;
        _preferences = preferences;
        _resetTokens = resetTokens;
        _email = email;
        _jwt = jwt;
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await _users.EmailExistsAsync(normalizedEmail))
        {
            return new AuthResult(AuthStatus.EmailAlreadyExists, null);
        }

        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = UserRole.Customer,
            CreatedAt = DateTime.UtcNow,
        };
        await _users.AddAsync(user);

        var token = _jwt.GenerateToken(user);
        // A brand-new account can never have preferences yet - skip the lookup.
        return new AuthResult(AuthStatus.Success, ToResponse(user, token, hasPreferences: false));
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByEmailAsync(normalizedEmail);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return new AuthResult(AuthStatus.InvalidCredentials, null);
        }

        var token = _jwt.GenerateToken(user);
        var hasPreferences = user.Role == UserRole.Customer && await _preferences.ExistsAsync(user.UserId);
        return new AuthResult(AuthStatus.Success, ToResponse(user, token, hasPreferences));
    }

    public async Task<(ThemeUpdateStatus Status, string? Theme)> UpdateThemeAsync(int userId, string theme)
    {
        if (!Enum.TryParse<ThemePreference>(theme, ignoreCase: true, out var newTheme))
        {
            return (ThemeUpdateStatus.InvalidTheme, null);
        }

        var user = await _users.GetByIdAsync(userId);
        if (user is null) return (ThemeUpdateStatus.UserNotFound, null);

        user.ThemePreference = newTheme;
        await _users.SaveChangesAsync();
        return (ThemeUpdateStatus.Success, ToThemeString(user.ThemePreference));
    }

    public async Task ForgotPasswordAsync(string email)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await _users.GetByEmailAsync(normalizedEmail);
        if (user is null)
        {
            // Deliberately do nothing and return silently - the caller (the
            // controller) always responds identically either way, so no
            // observable difference in behavior can reveal whether this
            // email is registered.
            return;
        }

        // A fresh request supersedes any still-live link from an earlier one.
        await _resetTokens.InvalidateActiveTokensForUserAsync(user.UserId);

        var (rawToken, tokenHash) = GenerateResetToken();
        await _resetTokens.AddAsync(new PasswordResetToken
        {
            UserId = user.UserId,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddMinutes(ResetTokenExpiryMinutes),
            CreatedAt = DateTime.UtcNow,
        });

        // Result intentionally discarded: a send failure can't be reported
        // back without leaking whether the account exists, so it's only
        // logged server-side (inside SmtpEmailService).
        await _email.SendPasswordResetAsync(user.UserId, rawToken);
    }

    // Lets the reset-password page tell a merely-present-but-dead token apart
    // from one worth showing the form for, without waiting for a full
    // password submission just to find out. Shares the exact same lookup as
    // ResetPasswordAsync so the two can never disagree about what "valid"
    // means.
    public async Task<bool> IsResetTokenValidAsync(string token)
    {
        var record = await FindValidTokenAsync(token);
        return record is not null;
    }

    public async Task<ResetPasswordStatus> ResetPasswordAsync(string token, string newPassword)
    {
        var record = await FindValidTokenAsync(token);
        if (record is null)
        {
            return ResetPasswordStatus.InvalidOrExpiredToken;
        }

        var user = await _users.GetByIdAsync(record.UserId);
        if (user is null)
        {
            return ResetPasswordStatus.InvalidOrExpiredToken;
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _users.SaveChangesAsync();

        // Sweeps up the token just used, plus any other stray active ones
        // for this account, in one call.
        await _resetTokens.InvalidateActiveTokensForUserAsync(user.UserId);

        return ResetPasswordStatus.Success;
    }

    private async Task<PasswordResetToken?> FindValidTokenAsync(string token)
    {
        string tokenHash;
        try
        {
            tokenHash = HashToken(token);
        }
        catch (FormatException)
        {
            // Not valid base64url - can't possibly match a stored hash.
            return null;
        }

        var record = await _resetTokens.GetByTokenHashAsync(tokenHash);
        if (record is null || record.UsedAt is not null || record.ExpiresAt < DateTime.UtcNow)
        {
            return null;
        }

        return record;
    }

    // 256 bits of randomness, base64url-encoded (no padding) for the emailed
    // link and SHA-256-hashed (hex) for storage - the raw token is never
    // persisted or logged, only ever held in memory long enough to email it.
    private static (string RawToken, string TokenHash) GenerateResetToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var tokenHash = HashToken(rawToken);
        return (rawToken, tokenHash);
    }

    private static string HashToken(string rawToken)
    {
        var padded = rawToken.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        var bytes = Convert.FromBase64String(padded);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static AuthResponse ToResponse(User user, string token, bool hasPreferences) =>
        new(token, user.UserId, user.FullName, user.Email, user.Role.ToString(), ToThemeString(user.ThemePreference), hasPreferences);

    // Unlike Role (frontend compares against "Organizer"/"Admin" PascalCase),
    // every other theme touchpoint - the DB enum, data-theme attribute,
    // localStorage value, TS type - is lowercase, so this normalizes the
    // C# enum's PascalCase ToString() to match rather than introducing a
    // second casing convention just for this one field.
    private static string ToThemeString(ThemePreference theme) => theme.ToString().ToLowerInvariant();
}
