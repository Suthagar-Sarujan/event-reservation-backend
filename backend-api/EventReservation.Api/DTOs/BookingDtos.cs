using System.ComponentModel.DataAnnotations;

namespace EventReservation.Api.DTOs;

public record CreateBookingRequest(
    [Required] string ListingId,
    [Range(1, 20)] int Quantity
);

public record BookingItemDto(string ListingId, string? Section, int Quantity, decimal UnitPrice, decimal Subtotal);

public record BookingResponseDto(
    int BookingId,
    string BookingReference,
    long EventId,
    string EventName,
    DateTime EventDatetimeUtc,
    string Status,
    decimal TotalAmount,
    DateTime CreatedAt,
    IReadOnlyList<BookingItemDto> Items,
    string? PaymentReference,
    DateTime? CheckedInAt
);

public record TicketDto(
    int BookingId,
    string BookingReference,
    string Status,
    long EventId,
    string EventName,
    DateTime EventDatetimeUtc,
    string VenueName,
    string? VenueCity,
    string? VenueState,
    int TotalQuantity,
    decimal TotalAmount,
    string? PaymentReference,
    DateTime? CheckedInAt,
    string QrCodeDataUri,
    IReadOnlyList<BookingItemDto> Items
);

public record VerifyTicketRequest([Required] string Code);

public record VerifyTicketResultDto(
    bool Found,
    bool SignatureValid,
    string? BookingReference,
    string? EventName,
    DateTime? EventDatetimeUtc,
    string? AttendeeName,
    string? AttendeeEmail,
    int? TotalQuantity,
    string? Status,
    bool AlreadyCheckedIn,
    DateTime? CheckedInAt,
    string Message
);
