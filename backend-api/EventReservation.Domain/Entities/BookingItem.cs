namespace EventReservation.Domain.Entities;

public class BookingItem
{
    public int BookingItemId { get; set; }
    public int BookingId { get; set; }
    public string ListingId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }

    public Booking? Booking { get; set; }
    public Listing? Listing { get; set; }
}
