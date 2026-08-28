using System.IdentityModel.Tokens.Jwt;
using EventReservation.Api.DTOs;
using EventReservation.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventReservation.Api.Controllers;

[ApiController]
[Route("api/organizer")]
[Authorize(Roles = "Organizer")]
public class OrganizerController : ControllerBase
{
    private readonly IOrganizerService _organizerService;

    public OrganizerController(IOrganizerService organizerService)
    {
        _organizerService = organizerService;
    }

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet("venues")]
    public async Task<ActionResult> GetVenues()
    {
        var venues = await _organizerService.GetVenuesAsync();
        return Ok(venues);
    }

    [HttpGet("events")]
    public async Task<ActionResult<List<OrganizerEventSummaryDto>>> GetMyEvents()
    {
        var events = await _organizerService.GetMyEventsAsync(CurrentUserId);
        return Ok(events);
    }

    [HttpGet("events/{id:long}")]
    public async Task<ActionResult<OrganizerEventDetailDto>> GetMyEvent(long id)
    {
        var e = await _organizerService.GetMyEventAsync(id, CurrentUserId);
        return e is null ? NotFound() : Ok(e);
    }

    [HttpPost("events")]
    public async Task<ActionResult<OrganizerEventDetailDto>> CreateEvent(CreateEventRequest request)
    {
        if (request.VenueId is null && request.NewVenue is null)
        {
            return BadRequest(new { message = "Provide either an existing venueId or newVenue details." });
        }

        var result = await _organizerService.CreateEventAsync(CurrentUserId, request);
        if (result.Status == OrganizerEventCreationStatus.VenueNotFound)
        {
            return BadRequest(new { message = "Selected venue does not exist." });
        }

        return Ok(result.Event);
    }

    [HttpPut("events/{id:long}")]
    public async Task<ActionResult> UpdateEvent(long id, UpdateEventRequest request)
    {
        var status = await _organizerService.UpdateEventAsync(id, CurrentUserId, request);
        return status switch
        {
            OrganizerUpdateStatus.NotFound => NotFound(),
            OrganizerUpdateStatus.InvalidStatus => BadRequest(new { message = "Status must be 'normal' or 'cancelled'." }),
            _ => NoContent(),
        };
    }

    [HttpPost("events/{id:long}/listings")]
    public async Task<ActionResult<OrganizerEventDetailDto>> AddListing(long id, CreateListingRequest request)
    {
        var e = await _organizerService.AddListingAsync(id, CurrentUserId, request);
        return e is null ? NotFound() : Ok(e);
    }

    [HttpPut("listings/{listingId}")]
    public async Task<ActionResult> UpdateListing(string listingId, UpdateListingRequest request)
    {
        var result = await _organizerService.UpdateListingAsync(listingId, CurrentUserId, request);
        return result.Status switch
        {
            OrganizerListingUpdateStatus.NotFound => NotFound(),
            OrganizerListingUpdateStatus.QuantityBelowSold =>
                BadRequest(new { message = $"Quantity can't be lower than the {result.SoldCount} tickets already sold." }),
            _ => NoContent(),
        };
    }

    [HttpGet("events/{id:long}/bookings")]
    public async Task<ActionResult<List<OrganizerBookingDto>>> GetEventBookings(long id)
    {
        var bookings = await _organizerService.GetEventBookingsAsync(id, CurrentUserId);
        return bookings is null ? NotFound() : Ok(bookings);
    }

    [HttpGet("sales-trend")]
    public async Task<ActionResult<List<TrendPointDto>>> GetSalesTrend([FromQuery] int days = 30)
    {
        var trend = await _organizerService.GetSalesTrendAsync(CurrentUserId, days);
        return Ok(trend);
    }

    // Scoped to events this organizer created - never other organizers'/users'
    // account or IP history, matching the same scoping already applied to
    // GetEventBookings.
    [HttpGet("fraud-overview")]
    public async Task<ActionResult<FraudOverviewDto>> GetFraudOverview()
    {
        var overview = await _organizerService.GetFraudOverviewAsync(CurrentUserId);
        return Ok(overview);
    }
}
