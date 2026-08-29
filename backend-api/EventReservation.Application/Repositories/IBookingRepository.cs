using EventReservation.Domain.Entities;

namespace EventReservation.Application.Repositories;

public interface IBookingRepository
{
    Task<List<Booking>> GetByUserAsync(int userId);

    /// <summary>Untracked, with Event/Venue/Items/Listing included - for the ticket/QR detail view. Null if the booking doesn't exist or isn't owned by this user.</summary>
    Task<Booking?> GetByIdForUserAsync(int bookingId, int userId);

    /// <summary>Untracked, with Event/User/Items included - for door-side ticket verification (not scoped to an owner, since the verifier is staff, not the ticket holder).</summary>
    Task<Booking?> GetForVerificationAsync(int bookingId);

    /// <summary>Same as <see cref="GetForVerificationAsync"/> but looked up by the human-readable booking reference, for manual entry when a QR can't be scanned.</summary>
    Task<Booking?> GetForVerificationByReferenceAsync(string bookingReference);

    /// <summary>
    /// Records the outcome of a booking-confirmation email attempt (see
    /// IEmailService/SmtpEmailService) - status, incremented attempt count,
    /// and (on success only) the sent timestamp. Never throws for a missing
    /// booking id; callers are expected to have already confirmed it exists.
    /// </summary>
    Task MarkEmailResultAsync(int bookingId, BookingEmailStatus status, int attempts, DateTime? sentAtUtc);

    /// <summary>True if the booking exists and belongs to an event this organizer created.</summary>
    Task<bool> IsBookingOnOrganizerEventAsync(int bookingId, int organizerUserId);

    /// <summary>
    /// Marks a booking checked in, atomically and only once: returns false
    /// (without changing anything) if it was already checked in, so a second
    /// scan of the same ticket is rejected rather than silently re-approved.
    /// </summary>
    Task<bool> TryMarkCheckedInAsync(int bookingId);

    /// <summary>
    /// Marks a booking checked out, atomically and only once: requires the
    /// booking to already be checked in and not yet checked out, returning
    /// false (without changing anything) otherwise - so check-out can't
    /// happen before check-in, and a second check-out of the same ticket is
    /// rejected rather than silently re-approved.
    /// </summary>
    Task<bool> TryMarkCheckedOutAsync(int bookingId);

    /// <summary>
    /// Cancels a confirmed booking as a single atomic operation: restores the
    /// listing's inventory, rolls back the per-(user, event) ticket count, and
    /// marks the booking Cancelled - all inside one transaction, mirroring the
    /// same concurrency-safe shape as <see cref="CreateAsync"/>.
    /// </summary>
    Task<BookingCancellationResult> CancelAsync(int bookingId, int userId);

    /// <summary>
    /// Books tickets for one listing as a single atomic operation: checks
    /// inventory, decrements it with a concurrency-safe guard, enforces the
    /// per-(user, event) ticket cap with the same kind of guard, and inserts
    /// the booking - all inside one transaction.
    /// </summary>
    Task<BookingCreationResult> CreateAsync(int userId, string listingId, int quantity, int maxTicketsPerEvent);

    /// <summary>Resolves a listing id to its event id without starting a booking - used to attach a fraud/risk log entry to the right event even when the attempt never reaches CreateAsync.</summary>
    Task<long?> GetEventIdForListingAsync(string listingId);

    Task<List<Booking>> GetByEventAsync(long eventId);
    Task<(int Total, List<Booking> Items)> AdminSearchAsync(string? search, int page, int pageSize);
    Task<int> CountAsync();
    Task<decimal> SumConfirmedRevenueAsync();
    Task<List<long>> GetConfirmedEventIdsForUserAsync(int userId);

    /// <summary>Confirmed bookings/revenue per day for the last <paramref name="days"/> days, platform-wide, zero-filled for days with no activity.</summary>
    Task<List<DailyTrendPoint>> GetDailyTrendAsync(int days);

    /// <summary>Same as <see cref="GetDailyTrendAsync"/>, scoped to bookings on events the given organizer created.</summary>
    Task<List<DailyTrendPoint>> GetDailyTrendForOrganizerAsync(int organizerUserId, int days);
}

public record DailyTrendPoint(DateTime Date, int Bookings, decimal Revenue);
