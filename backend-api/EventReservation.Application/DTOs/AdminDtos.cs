using System.ComponentModel.DataAnnotations;

namespace EventReservation.Application.DTOs;

public record AdminStatsDto(
    int TotalUsers,
    int TotalCustomers,
    int TotalOrganizers,
    int TotalAdmins,
    int TotalEvents,
    int TotalImportedEvents,
    int TotalOrganizerEvents,
    int TotalBookings,
    decimal TotalRevenue
);

public record AdminUserDto(int UserId, string FullName, string Email, string Role, DateTime CreatedAt);

public record UpdateUserRoleRequest([Required] string Role); // "customer" | "organizer" | "admin"

public record AdminEventDto(
    long EventId,
    string Name,
    DateTime DatetimeUtc,
    string VenueName,
    string Status,
    string Source, // "seatgeek" | "organizer"
    string? CreatorEmail,
    int TicketsSold,
    decimal Revenue,
    string? ImageUrl
);

public record AdminBookingDto(
    int BookingId,
    string BookingReference,
    string BuyerName,
    string BuyerEmail,
    long EventId,
    string EventName,
    int Quantity,
    decimal TotalAmount,
    string Status,
    DateTime CreatedAt,
    string EmailStatus,
    DateTime? EmailSentAt
);

public record TrendPointDto(DateTime Date, int Bookings, decimal Revenue);
