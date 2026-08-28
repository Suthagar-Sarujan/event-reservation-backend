using EventReservation.Application.DTOs;

namespace EventReservation.Application.Services;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request);
    Task<AuthResult> LoginAsync(LoginRequest request);
    Task<(ThemeUpdateStatus Status, string? Theme)> UpdateThemeAsync(int userId, string theme);
}
