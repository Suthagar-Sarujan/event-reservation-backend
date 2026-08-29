using System.ComponentModel.DataAnnotations;

namespace EventReservation.Application.DTOs;

public record RegisterRequest(
    [Required, StringLength(150, MinimumLength = 2)] string FullName,
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password
);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password
);

public record AuthResponse(string Token, int UserId, string FullName, string Email, string Role, string Theme, bool HasPreferences);

public record UpdateThemeRequest([Required] string Theme);

public record ThemeResponse(string Theme);

public record ForgotPasswordRequest([Required, EmailAddress] string Email);

public record ResetPasswordRequest([Required] string Token, [Required, MinLength(8)] string NewPassword);
