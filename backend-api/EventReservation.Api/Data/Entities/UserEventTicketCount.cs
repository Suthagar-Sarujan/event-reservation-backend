namespace EventReservation.Api.Data.Entities;

/// <summary>
/// Atomic per-(user, event) ticket counter used to enforce the max-tickets-per-
/// event booking cap race-condition-free - see BookingRepository.CreateAsync,
/// which mirrors the same conditional-UPDATE-then-check-rowsAffected pattern
/// already used there for listing inventory.
/// </summary>
public class UserEventTicketCount
{
    public int UserId { get; set; }
    public long EventId { get; set; }
    public int TicketsBooked { get; set; }
    public DateTime UpdatedAt { get; set; }
}
