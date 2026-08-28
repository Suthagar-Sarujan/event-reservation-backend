using EventReservation.Domain.Entities;

namespace EventReservation.Application.Repositories;

public enum BookingCreationStatus
{
    Success,
    ListingNotFound,
    InsufficientQuantity,
    SoldOutRace,
    TicketLimitExceeded,
    FraudBlocked,
}

/// <summary>
/// AvailableQuantity is only populated for InsufficientQuantity, to build the
/// "only N tickets remain" message without a second round trip.
/// </summary>
public record BookingCreationResult(BookingCreationStatus Status, Booking? Booking, int? AvailableQuantity);
