namespace EventReservation.Application.Services;

public enum EmailSendResult
{
    Sent,
    Failed,
    BookingNotFound,
}

public interface IEmailService
{
    /// <summary>
    /// Loads the booking (Event/User/Items included), builds and sends the
    /// HTML confirmation email with an inline QR code via SMTP, and records
    /// the outcome (Sent/Failed, timestamp, incremented attempt count) on the
    /// booking row. Never throws - always returns a result, logs failures
    /// internally, so a broken SMTP server can never fail an otherwise-
    /// successful booking or a resend request with an unhandled exception.
    /// </summary>
    Task<EmailSendResult> SendBookingConfirmationAsync(int bookingId);

    /// <summary>
    /// Emails a password-reset link built from the given raw (unhashed) token.
    /// The token is never logged or persisted by this method - it's already
    /// been hashed for storage by the caller before this runs. Never throws;
    /// returns false and logs (user id only) on any failure.
    /// </summary>
    Task<bool> SendPasswordResetAsync(int userId, string rawToken);
}
