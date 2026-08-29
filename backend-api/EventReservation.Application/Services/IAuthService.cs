using EventReservation.Application.DTOs;

namespace EventReservation.Application.Services;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request);
    Task<AuthResult> LoginAsync(LoginRequest request);
    Task<(ThemeUpdateStatus Status, string? Theme)> UpdateThemeAsync(int userId, string theme);

    /// <summary>
    /// Always "succeeds" from the caller's perspective regardless of whether
    /// the email matches an account - the anti-enumeration guarantee lives
    /// here, not in the controller, since this is the only layer that knows
    /// which case it was.
    /// </summary>
    Task ForgotPasswordAsync(string email);

    Task<ResetPasswordStatus> ResetPasswordAsync(string token, string newPassword);
}
