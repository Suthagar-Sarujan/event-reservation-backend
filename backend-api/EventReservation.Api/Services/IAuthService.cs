using EventReservation.Api.DTOs;

namespace EventReservation.Api.Services;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request);
    Task<AuthResult> LoginAsync(LoginRequest request);
    Task<(ThemeUpdateStatus Status, string? Theme)> UpdateThemeAsync(int userId, string theme);
}
