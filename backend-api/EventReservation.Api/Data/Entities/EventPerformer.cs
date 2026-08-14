namespace EventReservation.Api.Data.Entities;

public class EventPerformer
{
    public long EventId { get; set; }
    public int PerformerId { get; set; }

    public Event? Event { get; set; }
    public Performer? Performer { get; set; }
}
