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
    IReadOnlyList<BookingItemDto> Items
);
