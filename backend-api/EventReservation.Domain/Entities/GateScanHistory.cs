namespace EventReservation.Domain.Entities;

public enum GateScanType
{
    CheckIn,
    CheckOut,
}

public enum GateScanStatus
{
    Success,
    Failed,
}

/// <summary>
/// One row per scan attempt at a gate, success or failure, for audit -
/// mirrors BookingRiskAssessment's role as an append-only attempt log.
/// BookingId/EventId are nullable because a scan can fail before a booking
/// was ever resolved (e.g. malformed code, or a gate-permission rejection
/// that never even looks up a booking). GateId is nullable for the same
/// reason on the gate side: a scan request can name a gate id that doesn't
/// exist at all (stale client state, forged request), and the row still
/// needs to be logged without violating the FK to a row that isn't there.
/// </summary>
public class GateScanHistory
{
    public long ScanId { get; set; }
    public int? GateId { get; set; }
    public int ScannedByUserId { get; set; }
    public int? BookingId { get; set; }

    // Raw scanned text, kept for audit even on failure (e.g. a malformed or forged code).
    public string ScannedCode { get; set; } = string.Empty;
    public long? EventId { get; set; }
    public GateScanType ScanType { get; set; } = GateScanType.CheckIn;
    public GateScanStatus Status { get; set; }

    // Human-readable failure message text (e.g. "This ticket has already been used."), null on success.
    public string? FailureReason { get; set; }
    public DateTime ScannedAt { get; set; }

    public Gate? Gate { get; set; }
    public User? ScannedByUser { get; set; }
    public Booking? Booking { get; set; }
    public Event? Event { get; set; }
}
