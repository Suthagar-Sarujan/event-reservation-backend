using EventReservation.Application.DTOs;

namespace EventReservation.Application.Services;

public enum OrganizerEventCreationStatus
{
    Success,
    VenueNotFound,
}

public record OrganizerEventCreationResult(OrganizerEventCreationStatus Status, OrganizerEventDetailDto? Event);

public enum OrganizerUpdateStatus
{
    Success,
    NotFound,
    InvalidStatus,
}

public enum OrganizerListingUpdateStatus
{
    Success,
    NotFound,
    QuantityBelowSold,
}

public record OrganizerListingUpdateResult(OrganizerListingUpdateStatus Status, int? SoldCount);
