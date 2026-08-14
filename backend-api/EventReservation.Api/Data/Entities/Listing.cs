namespace EventReservation.Api.Data.Entities;

public enum ListingStatus
{
    Available,
    SoldOut,
}

public class Listing
{
    public string ListingId { get; set; } = string.Empty;
    public long EventId { get; set; }
    public string? Section { get; set; }
    public string? SectionFull { get; set; }
    public string? RowLabel { get; set; }
    public int Quantity { get; set; }
    public int QuantityRemaining { get; set; }
    public int? DealBucket { get; set; }
    public string? DeliveryType { get; set; }
    public string? Marketplace { get; set; }
    public string? SplitType { get; set; }
    public DateTime? InHandDate { get; set; }

    // Simulated price - see scripts/import_seatgeek_data.py for why this is derived
    // rather than sourced directly from SeatGeek (whose price fields are locked
    // behind a "[PREMIUM]" placeholder in the free dataset sample).
    public decimal UnitPrice { get; set; }
    public ListingStatus ListingStatus { get; set; }

    public Event? Event { get; set; }
}
