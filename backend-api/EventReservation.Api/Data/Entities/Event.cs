namespace EventReservation.Api.Data.Entities;

public class Event
{
    public long EventId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string? Type { get; set; }
    public string? TaxonomyName { get; set; }
    public string? TaxonomySubName { get; set; }
    public int VenueId { get; set; }
    public DateTime DatetimeUtc { get; set; }
    public DateTime? EndDatetimeUtc { get; set; }
    public bool DateTbd { get; set; }
    public bool TimeTbd { get; set; }
    public string? Status { get; set; }
    public string? ScheduleStatus { get; set; }
    public bool IsOpen { get; set; }
    public bool IsGa { get; set; }
    public bool SeatSelectionEnabled { get; set; }
    public string? Url { get; set; }
    public DateTime? CreatedAtSource { get; set; }
    public DateTime? AnnounceDate { get; set; }

    // NULL = imported from SeatGeek, set = created by an organizer through the app.
    public int? CreatedByUserId { get; set; }

    // Organizer-supplied event photo (they own the rights to it). SeatGeek-imported
    // events never have one - see README "Data limitations" for why.
    public string? ImageUrl { get; set; }

    public Venue? Venue { get; set; }
    public ICollection<EventPerformer> EventPerformers { get; set; } = new List<EventPerformer>();
    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
}
