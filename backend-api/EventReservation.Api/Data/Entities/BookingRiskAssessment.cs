namespace EventReservation.Api.Data.Entities;

public enum RiskLevel
{
    Low,
    Medium,
    High,
}

public enum RiskDecision
{
    Allowed,
    Flagged,
    Blocked,
}

/// <summary>
/// One row per booking attempt that was risk-evaluated (allowed, flagged, or
/// blocked). Covers both what the fraud-prevention brief calls "BookingRisk"
/// and "FraudDetectionLog" - they describe the same event, so this is one
/// table rather than two. IpAddress is stored for fraud/security review only:
/// never returned to the customer-facing API, only to Admin/Organizer
/// fraud-overview views.
/// </summary>
public class BookingRiskAssessment
{
    public long BookingRiskId { get; set; }
    public int UserId { get; set; }
    public long EventId { get; set; }

    // NULL when the attempt was blocked before a booking row ever existed.
    public int? BookingId { get; set; }
    public string? IpAddress { get; set; }
    public int RequestedQuantity { get; set; }
    public int RiskScore { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public RiskDecision Decision { get; set; }

    // Comma-joined signal codes, e.g. "high_booking_velocity,ip_multiple_accounts".
    public string Reasons { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
    public Event? Event { get; set; }
    public Booking? Booking { get; set; }
}
