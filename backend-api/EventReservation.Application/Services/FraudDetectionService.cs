using EventReservation.Domain.Entities;
using EventReservation.Application.Repositories;
using Microsoft.Extensions.Options;

namespace EventReservation.Application.Services;

/// <summary>
/// Composes a 0-100 risk score from a handful of independent signals (booking
/// velocity, IP address shared across accounts, prior blocked/flagged activity,
/// unusually large single-request quantity). Each signal's weight and every
/// threshold is configurable (see FraudOptions/appsettings.json "Fraud") rather
/// than hardcoded, so the sensitivity can be tuned without a code change.
/// </summary>
public class FraudDetectionService : IFraudDetectionService
{
    private readonly IFraudRepository _fraud;
    private readonly FraudOptions _options;

    public FraudDetectionService(IFraudRepository fraud, IOptions<FraudOptions> options)
    {
        _fraud = fraud;
        _options = options.Value;
    }

    public async Task<FraudEvaluation> EvaluateAsync(int userId, int quantity, string? ipAddress)
    {
        var reasons = new List<string>();
        var score = 0;

        var velocitySince = DateTime.UtcNow.AddMinutes(-_options.VelocityWindowMinutes);
        var recentBookings = await _fraud.CountRecentBookingsByUserAsync(userId, velocitySince);
        if (recentBookings >= _options.VelocityBookingThreshold)
        {
            score += 35;
            reasons.Add("high_booking_velocity");
        }

        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            var distinctUsersOnIp = await _fraud.CountDistinctUsersByIpAsync(ipAddress, DateTime.UtcNow.AddHours(-24));
            if (distinctUsersOnIp >= 2)
            {
                score += 30;
                reasons.Add("ip_multiple_accounts");
            }
        }

        var recentNonAllowed = await _fraud.CountRecentNonAllowedAsync(userId, ipAddress, DateTime.UtcNow.AddHours(-24));
        if (recentNonAllowed > 0)
        {
            score += Math.Min(25, recentNonAllowed * 10);
            reasons.Add("prior_blocked_or_flagged_activity");
        }

        if (quantity >= _options.LargeQuantityThreshold)
        {
            score += 15;
            reasons.Add("unusually_large_quantity");
        }

        score = Math.Min(score, 100);
        var level = ClassifyLevel(score);
        var decision = level switch
        {
            RiskLevel.High => RiskDecision.Blocked,
            RiskLevel.Medium => RiskDecision.Flagged,
            _ => RiskDecision.Allowed,
        };

        return new FraudEvaluation(decision, score, level, reasons);
    }

    private RiskLevel ClassifyLevel(int score)
    {
        if (score >= _options.RiskThresholds.HighMin) return RiskLevel.High;
        if (score >= _options.RiskThresholds.MediumMin) return RiskLevel.Medium;
        return RiskLevel.Low;
    }

    public Task LogAsync(int userId, long eventId, int? bookingId, string? ipAddress, int quantity, int riskScore, RiskLevel riskLevel, RiskDecision decision, IEnumerable<string> reasonCodes)
    {
        var reasonList = reasonCodes.ToList();
        return _fraud.LogAsync(new BookingRiskAssessment
        {
            UserId = userId,
            EventId = eventId,
            BookingId = bookingId,
            IpAddress = ipAddress,
            RequestedQuantity = quantity,
            RiskScore = riskScore,
            RiskLevel = riskLevel,
            Decision = decision,
            Reasons = reasonList.Count == 0 ? "none" : string.Join(",", reasonList),
            CreatedAt = DateTime.UtcNow,
        });
    }
}
