using EventReservation.Domain.Entities;
using EventReservation.Application.DTOs;
using EventReservation.Application.Repositories;
using EventReservation.Application.Services;
using Moq;
using Xunit;

namespace EventReservation.Api.Tests.Services;

public class OrganizerServiceTests
{
    private readonly Mock<IEventRepository> _events = new();
    private readonly Mock<IVenueRepository> _venues = new();
    private readonly Mock<IListingRepository> _listings = new();
    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<IRecommenderClient> _recommender = new();
    private readonly Mock<IFraudRepository> _fraud = new();
    private readonly OrganizerService _sut;

    public OrganizerServiceTests()
    {
        _sut = new OrganizerService(_events.Object, _venues.Object, _listings.Object, _bookings.Object, _recommender.Object, _fraud.Object);
    }

    private static CreateEventRequest MakeCreateRequest(int? venueId = 1) => new(
        "Test Event", "sports", "baseball", null, venueId, null, DateTime.UtcNow.AddDays(30), [], null);

    [Fact]
    public async Task CreateEventAsync_WhenVenueIdDoesNotExist_ReturnsVenueNotFoundWithoutTouchingEvents()
    {
        _venues.Setup(r => r.ExistsAsync(1)).ReturnsAsync(false);

        var result = await _sut.CreateEventAsync(10, MakeCreateRequest(venueId: 1));

        Assert.Equal(OrganizerEventCreationStatus.VenueNotFound, result.Status);
        Assert.Null(result.Event);
        _events.Verify(r => r.AddAsync(It.IsAny<Event>()), Times.Never);
    }

    [Fact]
    public async Task UpdateEventAsync_WhenEventNotOwnedByOrganizer_ReturnsNotFound()
    {
        _events.Setup(r => r.GetForOrganizerUpdateAsync(1, 10)).ReturnsAsync((Event?)null);

        var status = await _sut.UpdateEventAsync(1, 10, new UpdateEventRequest("New name", DateTime.UtcNow, "normal", null));

        Assert.Equal(OrganizerUpdateStatus.NotFound, status);
    }

    [Fact]
    public async Task UpdateEventAsync_WithInvalidStatus_ReturnsInvalidStatusWithoutSaving()
    {
        _events.Setup(r => r.GetForOrganizerUpdateAsync(1, 10)).ReturnsAsync(new Event { EventId = 1, CreatedByUserId = 10 });

        var status = await _sut.UpdateEventAsync(1, 10, new UpdateEventRequest("New name", DateTime.UtcNow, "bogus-status", null));

        Assert.Equal(OrganizerUpdateStatus.InvalidStatus, status);
        _events.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateEventAsync_WithValidStatus_SavesAndRefreshesTheRecommender()
    {
        var entity = new Event { EventId = 1, CreatedByUserId = 10 };
        _events.Setup(r => r.GetForOrganizerUpdateAsync(1, 10)).ReturnsAsync(entity);

        var status = await _sut.UpdateEventAsync(1, 10, new UpdateEventRequest("Renamed", DateTime.UtcNow, "cancelled", null));

        Assert.Equal(OrganizerUpdateStatus.Success, status);
        Assert.Equal("Renamed", entity.Name);
        Assert.Equal("cancelled", entity.Status);
        _events.Verify(r => r.SaveChangesAsync(), Times.Once);
        _recommender.Verify(r => r.RefreshAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateListingAsync_WhenNewQuantityIsBelowAlreadySoldCount_RejectsWithoutSaving()
    {
        var listing = new Listing { ListingId = "l1", Quantity = 10, QuantityRemaining = 2 }; // 8 already sold
        _listings.Setup(r => r.GetForOrganizerUpdateAsync("l1", 10)).ReturnsAsync(listing);

        var result = await _sut.UpdateListingAsync("l1", 10, new UpdateListingRequest(5, 20m));

        Assert.Equal(OrganizerListingUpdateStatus.QuantityBelowSold, result.Status);
        Assert.Equal(8, result.SoldCount);
        _listings.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateListingAsync_WithValidQuantity_UpdatesRemainingRelativeToAlreadySold()
    {
        var listing = new Listing { ListingId = "l1", Quantity = 10, QuantityRemaining = 4 }; // 6 already sold
        _listings.Setup(r => r.GetForOrganizerUpdateAsync("l1", 10)).ReturnsAsync(listing);

        var result = await _sut.UpdateListingAsync("l1", 10, new UpdateListingRequest(20, 30m));

        Assert.Equal(OrganizerListingUpdateStatus.Success, result.Status);
        Assert.Equal(20, listing.Quantity);
        Assert.Equal(14, listing.QuantityRemaining); // 20 - 6 already sold
        Assert.Equal(ListingStatus.Available, listing.ListingStatus);
    }

    [Fact]
    public async Task GetEventBookingsAsync_WhenEventNotOwnedByOrganizer_ReturnsNull()
    {
        _events.Setup(r => r.ExistsForOrganizerAsync(1, 10)).ReturnsAsync(false);

        var result = await _sut.GetEventBookingsAsync(1, 10);

        Assert.Null(result);
        _bookings.Verify(r => r.GetByEventAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task GetSalesTrendAsync_ScopesTheRepositoryCallToTheCallingOrganizer()
    {
        var today = DateTime.UtcNow.Date;
        _bookings.Setup(r => r.GetDailyTrendForOrganizerAsync(10, 14)).ReturnsAsync(new List<DailyTrendPoint>
        {
            new(today, 2, 90m),
        });

        var trend = await _sut.GetSalesTrendAsync(10, 14);

        Assert.Single(trend);
        Assert.Equal(90m, trend[0].Revenue);
        _bookings.Verify(r => r.GetDailyTrendForOrganizerAsync(10, 14), Times.Once);
    }
}
