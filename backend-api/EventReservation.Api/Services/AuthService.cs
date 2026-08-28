using EventReservation.Api.Data.Entities;
using EventReservation.Api.DTOs;
using EventReservation.Api.Repositories;

namespace EventReservation.Api.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IUserPreferenceRepository _preferences;
    private readonly JwtTokenService _jwt;

    public AuthService(IUserRepository users, IUserPreferenceRepository preferences, JwtTokenService jwt)
    {
        _users = users;
        _preferences = preferences;
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

    private static AuthResponse ToResponse(User user, string token, bool hasPreferences) =>
        new(token, user.UserId, user.FullName, user.Email, user.Role.ToString(), ToThemeString(user.ThemePreference), hasPreferences);

    // Unlike Role (frontend compares against "Organizer"/"Admin" PascalCase),
    // every other theme touchpoint - the DB enum, data-theme attribute,
    // localStorage value, TS type - is lowercase, so this normalizes the
    // C# enum's PascalCase ToString() to match rather than introducing a
    // second casing convention just for this one field.
    private static string ToThemeString(ThemePreference theme) => theme.ToString().ToLowerInvariant();
}
