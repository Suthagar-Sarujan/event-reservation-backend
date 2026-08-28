namespace EventReservation.Domain.Entities;

public class Performer
{
    public int PerformerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string? Type { get; set; }
    public string? Slug { get; set; }
    public string? TaxonomyName { get; set; }
    public string? TaxonomySubName { get; set; }
    public int? HomeVenueId { get; set; }
    public decimal? Score { get; set; }
    public int? Popularity { get; set; }
    public bool IsEvent { get; set; }
    public string? DivisionName { get; set; }
    public string? DivisionShortName { get; set; }

    // NULL = imported from SeatGeek, set = created by an organizer through the app.
    public int? CreatedByUserId { get; set; }

    public Venue? HomeVenue { get; set; }
    public ICollection<EventPerformer> EventPerformers { get; set; } = new List<EventPerformer>();
}
