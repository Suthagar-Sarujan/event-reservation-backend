namespace EventReservation.Api.Services;

public interface IQrCodeService
{
    /// <summary>
    /// A compact, tamper-evident token encoding a booking id, signed with an
    /// HMAC over the booking id and reference. No booking data is stored in
    /// the token itself beyond the id - the signature just proves the token
    /// was minted by this server for that exact booking, so a forged or
    /// edited token fails validation instead of silently verifying.
    /// </summary>
    string GenerateToken(int bookingId, string bookingReference);

    /// <summary>Validates a token against the given booking's current reference and returns the encoded booking id if it checks out.</summary>
    bool TryValidateToken(string token, string expectedBookingReference, out int bookingId);

    /// <summary>Best-effort extraction of the booking id from a token without validating the signature - used to look the booking up before checking it belongs to the token.</summary>
    bool TryReadBookingId(string token, out int bookingId);

    /// <summary>Renders the given content as a PNG QR code, returned as a data: URI ready for an &lt;img src&gt;.</summary>
    string GeneratePngDataUri(string content);
}
