using EventReservation.Api.DTOs;

namespace EventReservation.Api.Services;

public enum AuthStatus
{
    Success,
    EmailAlreadyExists,
    InvalidCredentials,
}

public record AuthResult(AuthStatus Status, AuthResponse? Response);

public enum ThemeUpdateStatus
{
    Success,
    InvalidTheme,
    UserNotFound,
}
