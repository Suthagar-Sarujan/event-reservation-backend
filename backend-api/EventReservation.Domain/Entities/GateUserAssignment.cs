namespace EventReservation.Domain.Entities;

/// <summary>
/// Join row granting a Gate User staff account permission to scan at a
/// specific Gate. A user can be assigned to multiple gates, and a gate can
/// have multiple assigned users.
/// </summary>
public class GateUserAssignment
{
    public int GateId { get; set; }
    public int UserId { get; set; }
    public DateTime AssignedAt { get; set; }
    public int? AssignedByUserId { get; set; }

    public Gate? Gate { get; set; }
    public User? User { get; set; }
}
