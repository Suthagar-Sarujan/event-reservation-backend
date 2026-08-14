namespace EventReservation.Api.Data.Entities;

public class Venue
{
    public int VenueId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? AddressStreet { get; set; }
    public string? AddressCity { get; set; }
    public string? AddressState { get; set; }
    public string? AddressCountry { get; set; }
    public string? AddressPostalCode { get; set; }
    public string? Timezone { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int? Capacity { get; set; }
    public decimal? PopularityScore { get; set; }
    public int? PopularityCount { get; set; }
    public int? MetroCode { get; set; }

    // NULL = imported from SeatGeek, set = created by an organizer through the app.
    public int? CreatedByUserId { get; set; }

    public ICollection<Performer> Performers { get; set; } = new List<Performer>();
    public ICollection<Event> Events { get; set; } = new List<Event>();
}
