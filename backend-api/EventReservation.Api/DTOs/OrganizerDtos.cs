using System.ComponentModel.DataAnnotations;

namespace EventReservation.Api.DTOs;

public record VenueOptionDto(int VenueId, string Name, string? AddressCity, string? AddressState);

public record CreateVenueRequest(
    [Required, StringLength(255, MinimumLength = 2)] string Name,
    string? AddressStreet,
    string? AddressCity,
    string? AddressState,
    string? AddressCountry,
    int? Capacity
);

public record CreateListingRequest(
    string? Section,
    [Range(1, 100000)] int Quantity,
    [Range(0, 100000)] decimal UnitPrice
);

public record CreateEventRequest(
    [Required, StringLength(500, MinimumLength = 2)] string Name,
    [Required] string TaxonomyName,
    [Required] string TaxonomySubName,
    string? PerformerName,
    int? VenueId,
    CreateVenueRequest? NewVenue,
    [Required] DateTime DatetimeUtc,
    List<CreateListingRequest> Listings,
    [Url, StringLength(500)] string? ImageUrl
);

public record UpdateEventRequest(
    [Required, StringLength(500, MinimumLength = 2)] string Name,
    [Required] DateTime DatetimeUtc,
    [Required] string Status, // "normal" or "cancelled"
    [Url, StringLength(500)] string? ImageUrl
);

public record UpdateListingRequest(
    [Range(0, 100000000)] int Quantity,
    [Range(0, 100000)] decimal UnitPrice
);

public record OrganizerEventSummaryDto(
    long EventId,
    string Name,
    DateTime DatetimeUtc,
    string VenueName,
    string Status,
    int ListingCount,
    int TicketsSold,
    int TicketsRemaining,
    decimal Revenue,
    string? ImageUrl
);

public record OrganizerListingDto(
    string ListingId,
    string? Section,
    int Quantity,
    int QuantityRemaining,
    decimal UnitPrice,
    string ListingStatus
);

public record OrganizerEventDetailDto(
    long EventId,
    string Name,
    string TaxonomyName,
    string TaxonomySubName,
    DateTime DatetimeUtc,
    string Status,
    string VenueName,
    IReadOnlyList<string> Performers,
    IReadOnlyList<OrganizerListingDto> Listings,
    int TicketsSold,
    decimal Revenue,
    string? ImageUrl
);

public record OrganizerBookingDto(
    int BookingId,
    string BookingReference,
    string BuyerName,
    string BuyerEmail,
    int Quantity,
    decimal TotalAmount,
    string Status,
    DateTime CreatedAt
);
