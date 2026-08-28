using EventReservation.Domain.Entities;
using EventReservation.Application.DTOs;
using EventReservation.Application.Repositories;
using EventReservation.Application.Services;
using Moq;
using Xunit;

namespace EventReservation.Api.Tests.Services;

public class EventServiceTests
{
    private readonly Mock<IEventRepository> _events = new();
    private readonly Mock<IRecommenderClient> _recommender = new();
    private readonly EventService _sut;

    public EventServiceTests()
    {
        _sut = new EventService(_events.Object, _recommender.Object);
    }

    private static EventSummaryDto MakeSummary(long id) =>
        new(id, $"Event {id}", "mlb", "sports", "baseball", DateTime.UtcNow.AddDays(1), "Venue", "City", "ST", 10m, 5, [], null);

    [Fact]
    public async Task SearchAsync_ClampsPageAndPageSizeBeforeDelegatingToRepository()
    {
        _events
            .Setup(r => r.SearchAsync(null, null, null, true, 1, 100))
            .ReturnsAsync((0, new List<EventSummaryDto>()));

        // page 0 -> clamped to 1; pageSize 500 -> clamped to the 100 ceiling.
        var (total, page, pageSize, items) = await _sut.SearchAsync(null, null, null, true, 0, 500);

        Assert.Equal(1, page);
        Assert.Equal(100, pageSize);
        _events.Verify(r => r.SearchAsync(null, null, null, true, 1, 100), Times.Once);
    }

    [Fact]
    public async Task GetDetailAsync_WhenEventMissing_ReturnsNull()
    {
        _events.Setup(r => r.GetDetailAsync(999)).ReturnsAsync((Event?)null);

        var result = await _sut.GetDetailAsync(999);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSimilarAsync_WhenSeedEventDoesNotExist_ReturnsNullWithoutCallingRecommender()
    {
        _events.Setup(r => r.ExistsAsync(404)).ReturnsAsync(false);

        var result = await _sut.GetSimilarAsync(404, 6);

        Assert.Null(result);
        _recommender.Verify(r => r.GetSimilarEventsAsync(It.IsAny<long>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetSimilarAsync_FiltersOutRecommendationsForEventsNoLongerInTheCatalog()
    {
        _events.Setup(r => r.ExistsAsync(1)).ReturnsAsync(true);
        _recommender
            .Setup(r => r.GetSimilarEventsAsync(1, 6))
            .ReturnsAsync(new RecommendationResponseDto(
                [new RecommendationItemDto(2, 0.9, "same performer"), new RecommendationItemDto(3, 0.5, "same venue")],
                false));
        // Event 3 no longer resolves to a summary (e.g. cancelled/removed) - should be dropped, not crash.
        _events.Setup(r => r.GetSummariesByIdsAsync(It.IsAny<IEnumerable<long>>()))
            .ReturnsAsync(new Dictionary<long, EventSummaryDto> { [2] = MakeSummary(2) });

        var result = await _sut.GetSimilarAsync(1, 6);

        Assert.NotNull(result);
        var single = Assert.Single(result!);
        Assert.Equal(2, single.Event.EventId);
    }
}
