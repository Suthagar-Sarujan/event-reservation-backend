using EventReservation.Domain.Entities;
using EventReservation.Application.Repositories;
using EventReservation.Application.Services;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EventReservation.Api.Tests.Services;

public class FraudDetectionServiceTests
{
    private readonly Mock<IFraudRepository> _fraud = new();
    private readonly FraudOptions _options = new();
    private readonly FraudDetectionService _sut;

    public FraudDetectionServiceTests()
    {
        _sut = new FraudDetectionService(_fraud.Object, Options.Create(_options));
        _fraud.Setup(r => r.CountRecentBookingsByUserAsync(It.IsAny<int>(), It.IsAny<DateTime>())).ReturnsAsync(0);
        _fraud.Setup(r => r.CountDistinctUsersByIpAsync(It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(0);
        _fraud.Setup(r => r.CountRecentNonAllowedAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime>())).ReturnsAsync(0);
    }

    [Fact]
    public async Task EvaluateAsync_WithNoSignalsTriggered_IsLowRiskAndAllowed()
    {
        var result = await _sut.EvaluateAsync(1, 1, "203.0.113.1");

        Assert.Equal(0, result.Score);
        Assert.Equal(RiskLevel.Low, result.Level);
        Assert.Equal(RiskDecision.Allowed, result.Decision);
        Assert.Empty(result.ReasonCodes);
    }

    [Fact]
    public async Task EvaluateAsync_WithHighBookingVelocity_AddsScoreAndReasonCode()
    {
        _fraud.Setup(r => r.CountRecentBookingsByUserAsync(1, It.IsAny<DateTime>())).ReturnsAsync(_options.VelocityBookingThreshold);

        var result = await _sut.EvaluateAsync(1, 1, null);

        Assert.Equal(35, result.Score);
        Assert.Contains("high_booking_velocity", result.ReasonCodes);
        Assert.Equal(RiskLevel.Medium, result.Level);
        Assert.Equal(RiskDecision.Flagged, result.Decision);
    }

    [Fact]
    public async Task EvaluateAsync_WithIpSharedAcrossAccounts_AddsScoreAndReasonCode()
    {
        _fraud.Setup(r => r.CountDistinctUsersByIpAsync("203.0.113.1", It.IsAny<DateTime>())).ReturnsAsync(3);

        var result = await _sut.EvaluateAsync(1, 1, "203.0.113.1");

        Assert.Equal(30, result.Score);
        Assert.Contains("ip_multiple_accounts", result.ReasonCodes);
    }

    [Fact]
    public async Task EvaluateAsync_WithoutIpAddress_SkipsIpSignalEntirely()
    {
        var result = await _sut.EvaluateAsync(1, 1, null);

        _fraud.Verify(r => r.CountDistinctUsersByIpAsync(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        Assert.DoesNotContain("ip_multiple_accounts", result.ReasonCodes);
    }

    [Fact]
    public async Task EvaluateAsync_WithLargeQuantity_AddsScoreAndReasonCode()
    {
        var result = await _sut.EvaluateAsync(1, _options.LargeQuantityThreshold, null);

        Assert.Equal(15, result.Score);
        Assert.Contains("unusually_large_quantity", result.ReasonCodes);
    }

    [Fact]
    public async Task EvaluateAsync_WhenCombinedSignalsCrossHighThreshold_IsBlocked()
    {
        _fraud.Setup(r => r.CountRecentBookingsByUserAsync(1, It.IsAny<DateTime>())).ReturnsAsync(_options.VelocityBookingThreshold);
        _fraud.Setup(r => r.CountDistinctUsersByIpAsync("203.0.113.1", It.IsAny<DateTime>())).ReturnsAsync(2);
        _fraud.Setup(r => r.CountRecentNonAllowedAsync(1, "203.0.113.1", It.IsAny<DateTime>())).ReturnsAsync(1);

        var result = await _sut.EvaluateAsync(1, 1, "203.0.113.1");

        // 35 (velocity) + 30 (ip reuse) + 10 (1 prior non-allowed) = 75 -> High/Blocked.
        Assert.Equal(75, result.Score);
        Assert.Equal(RiskLevel.High, result.Level);
        Assert.Equal(RiskDecision.Blocked, result.Decision);
    }

    [Fact]
    public async Task EvaluateAsync_ScoreNeverExceedsOneHundred()
    {
        _fraud.Setup(r => r.CountRecentBookingsByUserAsync(1, It.IsAny<DateTime>())).ReturnsAsync(_options.VelocityBookingThreshold);
        _fraud.Setup(r => r.CountDistinctUsersByIpAsync("203.0.113.1", It.IsAny<DateTime>())).ReturnsAsync(5);
        _fraud.Setup(r => r.CountRecentNonAllowedAsync(1, "203.0.113.1", It.IsAny<DateTime>())).ReturnsAsync(10);

        var result = await _sut.EvaluateAsync(1, _options.LargeQuantityThreshold, "203.0.113.1");

        Assert.Equal(100, result.Score);
    }

    [Fact]
    public async Task LogAsync_WithNoReasonCodes_StoresPlaceholderReasonText()
    {
        BookingRiskAssessment? captured = null;
        _fraud.Setup(r => r.LogAsync(It.IsAny<BookingRiskAssessment>()))
            .Callback<BookingRiskAssessment>(a => captured = a)
            .Returns(Task.CompletedTask);

        await _sut.LogAsync(1, 100L, 5, "203.0.113.1", 2, 0, RiskLevel.Low, RiskDecision.Allowed, []);

        Assert.NotNull(captured);
        Assert.Equal("none", captured!.Reasons);
        Assert.Equal(RiskDecision.Allowed, captured.Decision);
    }

    [Fact]
    public async Task LogAsync_WithReasonCodes_JoinsThemWithCommas()
    {
        BookingRiskAssessment? captured = null;
        _fraud.Setup(r => r.LogAsync(It.IsAny<BookingRiskAssessment>()))
            .Callback<BookingRiskAssessment>(a => captured = a)
            .Returns(Task.CompletedTask);

        await _sut.LogAsync(1, 100L, null, "203.0.113.1", 2, 75, RiskLevel.High, RiskDecision.Blocked, ["high_booking_velocity", "ticket_limit"]);

        Assert.Equal("high_booking_velocity,ticket_limit", captured!.Reasons);
    }
}
