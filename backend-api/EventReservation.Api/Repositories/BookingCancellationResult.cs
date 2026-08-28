namespace EventReservation.Api.Repositories;

public enum BookingCancellationStatus
{
    Success,
    NotFound,
    AlreadyCancelled,
    EventAlreadyOccurred,
    AlreadyCheckedIn,
}

public record BookingCancellationResult(BookingCancellationStatus Status);
