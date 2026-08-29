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

    /// <summary>Lets the reset-password page check a token before showing the form, without spending a submit attempt on it.</summary>
    Task<bool> IsResetTokenValidAsync(string token);

    Task<ResetPasswordStatus> ResetPasswordAsync(string token, string newPassword);
}
