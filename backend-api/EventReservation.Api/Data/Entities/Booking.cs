namespace EventReservation.Api.Data.Entities;

public enum BookingStatus
{
    Confirmed,
    Cancelled,
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

    public User? User { get; set; }
    public Event? Event { get; set; }
    public ICollection<BookingItem> Items { get; set; } = new List<BookingItem>();
}
