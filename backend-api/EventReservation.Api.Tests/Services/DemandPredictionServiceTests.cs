using EventReservation.Domain.Entities;
using EventReservation.Application.Repositories;
using EventReservation.Application.Services;
using Moq;
using Xunit;

namespace EventReservation.Api.Tests.Services;

public class DemandPredictionServiceTests
{
    private readonly Mock<IEventRepository> _events = new();
    private readonly Mock<IDemandClient> _demand = new();
    private readonly DemandPredictionService _sut;

    public DemandPredictionServiceTests()
    {
        _sut = new DemandPredictionService(_events.Object, _demand.Object);
    }

    private static DemandPredictionResponse MakeResponse(long eventId, double occupancy) =>
        new(eventId, $"Event {eventId}", DateTime.UtcNow.AddDays(10), 1000, 200, 400, occupancy, "MEDIUM");

    [Fact]
    public async Task GetForOrganizerAsync_WithNoEvents_SkipsTheRecommenderCallEntirely()
    {
        _events.Setup(r => r.GetByOrganizerAsync(5)).ReturnsAsync(new List<Event>());

        var result = await _sut.GetForOrganizerAsync(5);

        Assert.Empty(result);
        _demand.Verify(d => d.GetPredictionsAsync(It.IsAny<IReadOnlyList<long>>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task GetForOrganizerAsync_ScopesThePredictionRequestToTheOrganizersOwnEventIds()
    {
        _events.Setup(r => r.GetByOrganizerAsync(5)).ReturnsAsync(new List<Event>
        {
            new() { EventId = 10 },
            new() { EventId = 20 },
        });
        _demand
            .Setup(d => d.GetPredictionsAsync(It.Is<IReadOnlyList<long>>(l => l.SequenceEqual(new long[] { 10, 20 })), false))
            .ReturnsAsync(new List<DemandPredictionResponse> { MakeResponse(10, 0.9), MakeResponse(20, 0.3) });

        var result = await _sut.GetForOrganizerAsync(5);

        Assert.Equal(2, result.Count);
        Assert.Equal(10, result[0].EventId); // highest occupancy first
    }

    [Fact]
    public async Task GetForOrganizerEventAsync_WhenEventBelongsToAnotherOrganizer_ReturnsNullWithoutCallingTheRecommender()
    {
        _events.Setup(r => r.ExistsForOrganizerAsync(99, 5)).ReturnsAsync(false);

        var result = await _sut.GetForOrganizerEventAsync(99, 5);

        Assert.Null(result);
        _demand.Verify(d => d.GetPredictionAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task GetForOrganizerEventAsync_WhenOwned_ReturnsThePrediction()
    {
        _events.Setup(r => r.ExistsForOrganizerAsync(10, 5)).ReturnsAsync(true);
        _demand.Setup(d => d.GetPredictionAsync(10)).ReturnsAsync(MakeResponse(10, 0.75));

        var result = await _sut.GetForOrganizerEventAsync(10, 5);

        Assert.NotNull(result);
        Assert.Equal("MEDIUM", result!.DemandLevel);
    }
}
