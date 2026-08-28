using EventReservation.Api.DTOs;
using EventReservation.Api.Repositories;
using EventReservation.Api.Services;
using Moq;
using Xunit;

namespace EventReservation.Api.Tests.Services;

public class RecommendationServiceTests
{
    private readonly Mock<IEventRepository> _events = new();
    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<IUserPreferenceRepository> _preferences = new();
    private readonly Mock<IRecommenderClient> _recommender = new();
    private readonly RecommendationService _sut;

    public RecommendationServiceTests()
    {
        _preferences.Setup(r => r.GetByUserIdAsync(It.IsAny<int>())).ReturnsAsync((Data.Entities.UserPreference?)null);
        _sut = new RecommendationService(_events.Object, _bookings.Object, _preferences.Object, _recommender.Object);
    }

    private static EventSummaryDto MakeSummary(long id) =>
        new(id, $"Event {id}", "mlb", "sports", "baseball", DateTime.UtcNow.AddDays(1), "Venue", "City", "ST", 10m, 5, [], null);

    [Fact]
    public async Task GetForYouAsync_ForAnonymousVisitor_SkipsBookingLookupAndRequestsNonPersonalized()
    {
        _recommender
            .Setup(r => r.GetRecommendationsForUserAsync(
                It.Is<IReadOnlyList<long>>(l => l.Count == 0), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(), 10))
            .ReturnsAsync(new RecommendationResponseDto([], false));

        await _sut.GetForYouAsync(userId: null, topN: 10);

        _bookings.Verify(r => r.GetConfirmedEventIdsForUserAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetForYouAsync_ForLoggedInUser_PassesTheirConfirmedBookingsAsTheProfile()
    {
        _bookings.Setup(r => r.GetConfirmedEventIdsForUserAsync(5)).ReturnsAsync([10L, 20L]);
        _recommender
            .Setup(r => r.GetRecommendationsForUserAsync(
                It.Is<IReadOnlyList<long>>(l => l.SequenceEqual(new long[] { 10, 20 })),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(), 10))
            .ReturnsAsync(new RecommendationResponseDto([], false));

        await _sut.GetForYouAsync(userId: 5, topN: 10);

        _bookings.Verify(r => r.GetConfirmedEventIdsForUserAsync(5), Times.Once);
    }

    [Fact]
    public async Task GetForYouAsync_HydratesRankedEventIdsIntoFullSummariesInOrder()
    {
        _bookings.Setup(r => r.GetConfirmedEventIdsForUserAsync(1)).ReturnsAsync([]);
        _recommender
            .Setup(r => r.GetRecommendationsForUserAsync(
                It.IsAny<IReadOnlyList<long>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<int>()))
            .ReturnsAsync(new RecommendationResponseDto(
                [new RecommendationItemDto(2, 0.9, "reason A"), new RecommendationItemDto(1, 0.5, "reason B")],
                true));
        _events.Setup(r => r.GetSummariesByIdsAsync(It.IsAny<IEnumerable<long>>()))
            .ReturnsAsync(new Dictionary<long, EventSummaryDto> { [1] = MakeSummary(1), [2] = MakeSummary(2) });

        var result = await _sut.GetForYouAsync(userId: 1, topN: 10);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Event.EventId); // ranking order preserved, not id order
        Assert.Equal("reason A", result[0].Reason);
    }

    [Fact]
    public async Task GetForYouAsync_WithStoredPreferences_ForwardsThemToTheRecommender()
    {
        _bookings.Setup(r => r.GetConfirmedEventIdsForUserAsync(9)).ReturnsAsync([]);
        _preferences.Setup(r => r.GetByUserIdAsync(9)).ReturnsAsync(new Data.Entities.UserPreference
        {
            UserId = 9,
            EventTypes = "Music Concerts,Sports",
            MusicGenres = "Rock,EDM",
        });
        _recommender
            .Setup(r => r.GetRecommendationsForUserAsync(
                It.IsAny<IReadOnlyList<long>>(),
                It.Is<IReadOnlyList<string>>(l => l.SequenceEqual(new[] { "Music Concerts", "Sports" })),
                It.Is<IReadOnlyList<string>>(l => l.SequenceEqual(new[] { "Rock", "EDM" })),
                10))
            .ReturnsAsync(new RecommendationResponseDto([], true))
            .Verifiable();

        await _sut.GetForYouAsync(userId: 9, topN: 10);

        _recommender.Verify();
    }

    [Fact]
    public async Task GetPopularAsync_DoesNotTouchBookingHistory()
    {
        _recommender.Setup(r => r.GetPopularEventsAsync(8)).ReturnsAsync(new RecommendationResponseDto([], false));

        await _sut.GetPopularAsync(8);

        _bookings.Verify(r => r.GetConfirmedEventIdsForUserAsync(It.IsAny<int>()), Times.Never);
    }
}
