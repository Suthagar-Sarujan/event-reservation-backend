using EventReservation.Domain.Entities;
using EventReservation.Application.DTOs;
using EventReservation.Application.Repositories;
using Microsoft.Extensions.Options;

namespace EventReservation.Application.Services;

public class BookingService : IBookingService
{
    private readonly IBookingRepository _bookings;
    private readonly IFraudDetectionService _fraud;
    private readonly IQrCodeService _qr;
    private readonly FraudOptions _fraudOptions;

    public BookingService(IBookingRepository bookings, IFraudDetectionService fraud, IQrCodeService qr, IOptions<FraudOptions> fraudOptions)
    {
        _bookings = bookings;
        _fraud = fraud;
        _qr = qr;
        _fraudOptions = fraudOptions.Value;
    }

    public async Task<List<BookingResponseDto>> GetMyBookingsAsync(int userId)
    {
        var bookings = await _bookings.GetByUserAsync(userId);
        return bookings.Select(ToDto).ToList();
    }

    public async Task<(BookingCreationStatus Status, BookingResponseDto? Booking, int? AvailableQuantity)> CreateBookingAsync(int userId, CreateBookingRequest request, string? ipAddress)
    {
        var eventId = await _bookings.GetEventIdForListingAsync(request.ListingId);
        if (eventId is null)
        {
            return (BookingCreationStatus.ListingNotFound, null, null);
        }

        // Account/IP behaviour is scored before the booking is even attempted -
        // a high-risk attempt never touches inventory or the ticket cap.
        var evaluation = await _fraud.EvaluateAsync(userId, request.Quantity, ipAddress);
        if (evaluation.Decision == RiskDecision.Blocked)
        {
            await _fraud.LogAsync(userId, eventId.Value, null, ipAddress, request.Quantity,
                evaluation.Score, evaluation.Level, RiskDecision.Blocked, evaluation.ReasonCodes);
            return (BookingCreationStatus.FraudBlocked, null, null);
        }

        var result = await _bookings.CreateAsync(userId, request.ListingId, request.Quantity, _fraudOptions.MaxTicketsPerUserPerEvent);

        if (result.Status == BookingCreationStatus.TicketLimitExceeded)
        {
            var reasons = evaluation.ReasonCodes.Append("ticket_limit");
            await _fraud.LogAsync(userId, eventId.Value, null, ipAddress, request.Quantity,
                evaluation.Score, evaluation.Level, RiskDecision.Blocked, reasons);
            return (BookingCreationStatus.TicketLimitExceeded, null, null);
        }

        if (result.Status == BookingCreationStatus.Success)
        {
            await _fraud.LogAsync(userId, eventId.Value, result.Booking!.BookingId, ipAddress, request.Quantity,
                evaluation.Score, evaluation.Level, evaluation.Decision, evaluation.ReasonCodes);
        }

        var dto = result.Booking is null ? null : ToDto(result.Booking);
        return (result.Status, dto, result.AvailableQuantity);
    }

    public async Task<TicketDto?> GetTicketAsync(int bookingId, int userId)
    {
        var booking = await _bookings.GetByIdForUserAsync(bookingId, userId);
        if (booking is null || booking.Event is null) return null;

        var token = _qr.GenerateToken(booking.BookingId, booking.BookingReference);
        var qrDataUri = _qr.GeneratePngDataUri(token);

        return new TicketDto(
            booking.BookingId,
            booking.BookingReference,
            booking.Status.ToString(),
            booking.EventId,
            booking.Event.Name,
            booking.Event.DatetimeUtc,
            booking.Event.Venue?.Name ?? string.Empty,
            booking.Event.Venue?.AddressCity,
            booking.Event.Venue?.AddressState,
            booking.Items.Sum(i => i.Quantity),
            booking.TotalAmount,
            booking.PaymentReference,
            booking.CheckedInAt,
            qrDataUri,
            booking.Items.Select(i => new BookingItemDto(i.ListingId, i.Listing?.SectionFull ?? i.Listing?.Section, i.Quantity, i.UnitPrice, i.Subtotal)).ToList()
        );
    }

    public async Task<BookingCancellationStatus> CancelBookingAsync(int bookingId, int userId)
    {
        var result = await _bookings.CancelAsync(bookingId, userId);
        return result.Status;
    }

    public async Task<VerifyTicketResultDto> VerifyTicketAsync(string code)
    {
        code = code.Trim();
        var looksLikeSignedToken = code.Contains('.');

        Booking? booking;
        bool signatureValid;

        if (looksLikeSignedToken && _qr.TryReadBookingId(code, out var bookingId))
        {
            booking = await _bookings.GetForVerificationAsync(bookingId);
            if (booking is null)
            {
                return NotFoundResult();
            }
            signatureValid = _qr.TryValidateToken(code, booking.BookingReference, out _);
            if (!signatureValid)
            {
                // A booking with this id exists, but the signature doesn't match -
                // never surface attendee details for a code that fails
                // cryptographic verification, only that it was rejected.
                return new VerifyTicketResultDto(true, false, null, null, null, null, null, null, null,
                    false, null, "Invalid or altered code. This ticket could not be cryptographically verified and was rejected.");
            }
        }
        else
        {
            booking = await _bookings.GetForVerificationByReferenceAsync(code);
            if (booking is null)
            {
                return NotFoundResult();
            }
            signatureValid = false;
        }

        if (booking.Event is null || booking.User is null)
        {
            return NotFoundResult();
        }

        if (booking.Status == BookingStatus.Cancelled)
        {
            return new VerifyTicketResultDto(true, signatureValid, booking.BookingReference, booking.Event.Name,
                booking.Event.DatetimeUtc, booking.User.FullName, booking.User.Email, booking.Items.Sum(i => i.Quantity),
                booking.Status.ToString(), false, null, "This booking was cancelled and is not valid for entry.");
        }

        if (booking.CheckedInAt is not null)
        {
            return new VerifyTicketResultDto(true, signatureValid, booking.BookingReference, booking.Event.Name,
                booking.Event.DatetimeUtc, booking.User.FullName, booking.User.Email, booking.Items.Sum(i => i.Quantity),
                booking.Status.ToString(), true, booking.CheckedInAt,
                $"Already checked in at {booking.CheckedInAt:g}. This ticket cannot be used again.");
        }

        var markedIn = await _bookings.TryMarkCheckedInAsync(booking.BookingId);
        var checkedInAt = markedIn ? DateTime.UtcNow : booking.CheckedInAt;

        return new VerifyTicketResultDto(true, signatureValid, booking.BookingReference, booking.Event.Name,
            booking.Event.DatetimeUtc, booking.User.FullName, booking.User.Email, booking.Items.Sum(i => i.Quantity),
            booking.Status.ToString(), !markedIn, checkedInAt,
            markedIn ? "Valid ticket. Checked in successfully." : "Already checked in. This ticket cannot be used again.");
    }

    private static VerifyTicketResultDto NotFoundResult() =>
        new(false, false, null, null, null, null, null, null, null, false, null, "No booking found for this code.");

    private static BookingResponseDto ToDto(Booking b) => new(
        b.BookingId,
        b.BookingReference,
        b.EventId,
        b.Event?.Name ?? string.Empty,
        b.Event?.DatetimeUtc ?? default,
        b.Status.ToString(),
        b.TotalAmount,
        b.CreatedAt,
        b.Items.Select(i => new BookingItemDto(i.ListingId, null, i.Quantity, i.UnitPrice, i.Subtotal)).ToList(),
        b.PaymentReference,
        b.CheckedInAt
    );
}
