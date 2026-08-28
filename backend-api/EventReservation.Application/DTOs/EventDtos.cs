namespace EventReservation.Application.DTOs;

public record EventSummaryDto(
    long EventId,
    string Name,
    string? Type,
    string? TaxonomyName,
    string? TaxonomySubName,
    DateTime DatetimeUtc,
    string VenueName,
    string? VenueCity,
    string? VenueState,
    decimal? MinPrice,
    int TicketsRemaining,
    IReadOnlyList<string> Performers,
    string? ImageUrl
);

public record ListingDto(
    string ListingId,
    string? Section,
    string? SectionFull,
    string? RowLabel,
    int QuantityRemaining,
    string? DeliveryType,
    decimal UnitPrice
);

public record EventDetailDto(
    long EventId,
    string Name,
    string? Type,
    string? TaxonomyName,
    string? TaxonomySubName,
    DateTime DatetimeUtc,
    string VenueName,
    string? VenueAddress,
    string? VenueCity,
    string? VenueState,
    string? VenueCountry,
    int? VenueCapacity,
    IReadOnlyList<string> Performers,
    IReadOnlyList<ListingDto> Listings,
    string? ImageUrl
);

public record RecommendedEventDto(EventSummaryDto Event, double Score, string Reason);
