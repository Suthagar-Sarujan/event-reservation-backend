using EventReservation.Api.Data.Entities;

namespace EventReservation.Api.Services;

public record FraudEvaluation(RiskDecision Decision, int Score, RiskLevel Level, IReadOnlyList<string> ReasonCodes);

public interface IFraudDetectionService
{
    /// <summary>
    /// Scores a booking attempt from account/IP behaviour signals alone (not
    /// tied to a specific event - the hard per-event ticket cap is enforced
    /// separately, atomically, inside BookingRepository.CreateAsync).
    /// </summary>
    Task<FraudEvaluation> EvaluateAsync(int userId, int quantity, string? ipAddress);

    Task LogAsync(int userId, long eventId, int? bookingId, string? ipAddress, int quantity, int riskScore, RiskLevel riskLevel, RiskDecision decision, IEnumerable<string> reasonCodes);
}
