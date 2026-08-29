namespace EventReservation.Domain.Entities;

public enum GateStatus
{
    Active,
    Inactive,
}

/// <summary>
/// A venue-level physical entry point (e.g. "Gate A"). Deliberately NOT tied
/// to a single Event via a foreign key - it's reusable across events, and a
/// scan session instead supplies the eventId client-side (see GateService.
/// ScanTicketAsync), which checks the ticket's booking belongs to that event.
/// </summary>
public class Gate
{
    public int GateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public GateStatus Status { get; set; } = GateStatus.Active;
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<GateUserAssignment> Assignments { get; set; } = new List<GateUserAssignment>();
}
