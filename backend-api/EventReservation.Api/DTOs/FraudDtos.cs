namespace EventReservation.Api.DTOs;

public record RiskAssessmentDto(
    long BookingRiskId,
    int UserId,
    string UserEmail,
    long EventId,
    string EventName,
    int? BookingId,
    string? IpAddress,
    int RequestedQuantity,
    int RiskScore,
    string RiskLevel,
    string Decision,
    string Reasons,
    DateTime CreatedAt
);

public record FraudOverviewDto(
    int BlockedToday,
    int FlaggedToday,
    int TotalAssessed,
    List<RiskAssessmentDto> Recent
);
