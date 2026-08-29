using EventReservation.Application.DTOs;
using EventReservation.Application.Repositories;

namespace EventReservation.Application.Services;

public interface IBookingService
{
    Task<List<BookingResponseDto>> GetMyBookingsAsync(int userId);
    Task<(BookingCreationStatus Status, BookingResponseDto? Booking, int? AvailableQuantity)> CreateBookingAsync(int userId, CreateBookingRequest request, string? ipAddress);

    /// <summary>Null if the booking doesn't exist or isn't owned by this user.</summary>
    Task<TicketDto?> GetTicketAsync(int bookingId, int userId);

    Task<BookingCancellationStatus> CancelBookingAsync(int bookingId, int userId);

    Task<VerifyTicketResultDto> VerifyTicketAsync(string code);

    /// <summary>Resends the booking confirmation email. Returns BookingNotFound if the booking doesn't exist or isn't owned by this user (never distinguishing the two, to avoid leaking existence of other users' bookings).</summary>
    Task<EmailSendResult> ResendConfirmationEmailAsync(int bookingId, int userId);
}
