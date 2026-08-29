using EventReservation.Application.DTOs;

namespace EventReservation.Application.Services;

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

// Missing, expired, and already-used tokens all collapse to the same status
// deliberately - never let the response distinguish which, or a caller could
// probe for which reset links have or haven't been consumed.
public enum ResetPasswordStatus
{
    Success,
    InvalidOrExpiredToken,
}
