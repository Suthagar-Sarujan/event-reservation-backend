namespace EventReservation.Domain.Entities;

public enum BookingStatus
{
    Confirmed,
    Cancelled,
}

public enum BookingEmailStatus
{
    Pending,
    Sent,
    Failed,
}

public class Booking
{
    public int BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public int UserId { get; set; }
    public long EventId { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Confirmed;
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }

    // Mock payment confirmation id (e.g. "PAY-XXXXXXXX") - no real gateway is
    // integrated, see README/proposal Limitations; this exists purely so the
    // simulated payment step has something concrete to show the customer.
    public string? PaymentReference { get; set; }

    // Set the first time a ticket is scanned/verified at the door (see
    // TicketVerificationService). Null means not yet checked in. A second
    // verification attempt is rejected rather than silently re-approved.
    public DateTime? CheckedInAt { get; set; }

    // Set when a gate user checks the ticket back out (see GateService).
    // Null means not yet checked out. Only settable once CheckedInAt is set,
    // and only once - a second check-out attempt is rejected the same way a
    // second check-in is.
    public DateTime? CheckedOutAt { get; set; }

    // Tracks the booking-confirmation email (see IEmailService/SmtpEmailService).
    // Sending never blocks or fails a booking - these fields are best-effort
    // status for the customer/Admin/Organizer to see and, if needed, resend from.
    public BookingEmailStatus EmailStatus { get; set; } = BookingEmailStatus.Pending;
    public DateTime? EmailSentAt { get; set; }
    public int EmailAttempts { get; set; } = 0;

    public User? User { get; set; }
    public Event? Event { get; set; }
    public ICollection<BookingItem> Items { get; set; } = new List<BookingItem>();
}
