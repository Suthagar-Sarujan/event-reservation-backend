using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using EventReservation.Api.Data;
using EventReservation.Api.Data.Entities;
using EventReservation.Api.DTOs;
using EventReservation.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventReservation.Api.Controllers;

[ApiController]
[Route("api/organizer")]
[Authorize(Roles = "Organizer")]
public class OrganizerController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly RecommenderClient _recommender;

    public OrganizerController(AppDbContext db, RecommenderClient recommender)
    {
        _db = db;
        _recommender = recommender;
    }

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    private static string GenerateListingId()
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
        Span<byte> bytes = stackalloc byte[10];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[10];
        for (var i = 0; i < 10; i++) chars[i] = alphabet[bytes[i] % alphabet.Length];
        return "org-" + new string(chars);
    }

    [HttpGet("venues")]
    public async Task<ActionResult> GetVenues()
    {
        var venues = await _db.Venues.AsNoTracking()
            .OrderBy(v => v.Name)
            .Select(v => new { v.VenueId, v.Name, v.AddressCity, v.AddressState })
            .ToListAsync();
        return Ok(venues);
    }

    [HttpGet("events")]
    public async Task<ActionResult<List<OrganizerEventSummaryDto>>> GetMyEvents()
    {
        var events = await _db.Events.AsNoTracking()
            .Where(e => e.CreatedByUserId == CurrentUserId)
            .Include(e => e.Venue)
            .Include(e => e.Listings)
            .OrderByDescending(e => e.DatetimeUtc)
            .ToListAsync();

        var result = events.Select(e => new OrganizerEventSummaryDto(
            e.EventId,
            e.Name,
            e.DatetimeUtc,
            e.Venue!.Name,
            e.Status ?? "normal",
            e.Listings.Count,
            e.Listings.Sum(l => l.Quantity - l.QuantityRemaining),
            e.Listings.Sum(l => l.QuantityRemaining),
            e.Listings.Sum(l => (l.Quantity - l.QuantityRemaining) * l.UnitPrice),
            e.ImageUrl
        )).ToList();

        return Ok(result);
    }

    [HttpGet("events/{id:long}")]
    public async Task<ActionResult<OrganizerEventDetailDto>> GetMyEvent(long id)
    {
        var e = await _db.Events.AsNoTracking()
            .Include(ev => ev.Venue)
            .Include(ev => ev.EventPerformers).ThenInclude(ep => ep.Performer)
            .Include(ev => ev.Listings)
            .FirstOrDefaultAsync(ev => ev.EventId == id && ev.CreatedByUserId == CurrentUserId);

        if (e is null) return NotFound();

        var dto = new OrganizerEventDetailDto(
            e.EventId,
            e.Name,
            e.TaxonomyName ?? "",
            e.TaxonomySubName ?? "",
            e.DatetimeUtc,
            e.Status ?? "normal",
            e.Venue!.Name,
            e.EventPerformers.Select(ep => ep.Performer!.Name).ToList(),
            e.Listings.Select(l => new OrganizerListingDto(
                l.ListingId, l.Section, l.Quantity, l.QuantityRemaining, l.UnitPrice, l.ListingStatus.ToString())).ToList(),
            e.Listings.Sum(l => l.Quantity - l.QuantityRemaining),
            e.Listings.Sum(l => (l.Quantity - l.QuantityRemaining) * l.UnitPrice),
            e.ImageUrl
        );

        return Ok(dto);
    }

    [HttpPost("events")]
    public async Task<ActionResult<OrganizerEventDetailDto>> CreateEvent(CreateEventRequest request)
    {
        if (request.VenueId is null && request.NewVenue is null)
        {
            return BadRequest(new { message = "Provide either an existing venueId or newVenue details." });
        }

        int venueId;
        if (request.VenueId is not null)
        {
            var venueExists = await _db.Venues.AnyAsync(v => v.VenueId == request.VenueId);
            if (!venueExists) return BadRequest(new { message = "Selected venue does not exist." });
            venueId = request.VenueId.Value;
        }
        else
        {
            var venue = new Venue
            {
                Name = request.NewVenue!.Name,
                AddressStreet = request.NewVenue.AddressStreet,
                AddressCity = request.NewVenue.AddressCity,
                AddressState = request.NewVenue.AddressState,
                AddressCountry = request.NewVenue.AddressCountry,
                Capacity = request.NewVenue.Capacity,
                CreatedByUserId = CurrentUserId,
            };
            _db.Venues.Add(venue);
            await _db.SaveChangesAsync();
            venueId = venue.VenueId;
        }

        var eventType = request.TaxonomySubName.Trim().ToLowerInvariant().Replace(' ', '_');
        var newEvent = new Event
        {
            Name = request.Name,
            ShortName = request.Name,
            Type = eventType,
            TaxonomyName = request.TaxonomyName.Trim().ToLowerInvariant(),
            TaxonomySubName = request.TaxonomySubName.Trim().ToLowerInvariant(),
            VenueId = venueId,
            DatetimeUtc = request.DatetimeUtc,
            Status = "normal",
            ScheduleStatus = "as_originally_scheduled",
            IsOpen = true,
            IsGa = true,
            SeatSelectionEnabled = false,
            CreatedByUserId = CurrentUserId,
            ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim(),
        };
        _db.Events.Add(newEvent);
        await _db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(request.PerformerName))
        {
            var performer = new Performer
            {
                Name = request.PerformerName.Trim(),
                ShortName = request.PerformerName.Trim(),
                Type = eventType,
                TaxonomyName = newEvent.TaxonomyName,
                TaxonomySubName = newEvent.TaxonomySubName,
                Score = 0,
                Popularity = 0,
                IsEvent = false,
                CreatedByUserId = CurrentUserId,
            };
            _db.Performers.Add(performer);
            await _db.SaveChangesAsync();
            _db.EventPerformers.Add(new EventPerformer { EventId = newEvent.EventId, PerformerId = performer.PerformerId });
        }

        foreach (var listingRequest in request.Listings)
        {
            _db.Listings.Add(new Listing
            {
                ListingId = GenerateListingId(),
                EventId = newEvent.EventId,
                Section = string.IsNullOrWhiteSpace(listingRequest.Section) ? "General Admission" : listingRequest.Section,
                Quantity = listingRequest.Quantity,
                QuantityRemaining = listingRequest.Quantity,
                UnitPrice = listingRequest.UnitPrice,
                ListingStatus = listingRequest.Quantity > 0 ? ListingStatus.Available : ListingStatus.SoldOut,
            });
        }
        await _db.SaveChangesAsync();

        await _recommender.RefreshAsync();

        return await GetMyEvent(newEvent.EventId);
    }

    [HttpPut("events/{id:long}")]
    public async Task<ActionResult> UpdateEvent(long id, UpdateEventRequest request)
    {
        var e = await _db.Events.FirstOrDefaultAsync(ev => ev.EventId == id && ev.CreatedByUserId == CurrentUserId);
        if (e is null) return NotFound();

        if (request.Status is not ("normal" or "cancelled"))
        {
            return BadRequest(new { message = "Status must be 'normal' or 'cancelled'." });
        }

        e.Name = request.Name;
        e.DatetimeUtc = request.DatetimeUtc;
        e.Status = request.Status;
        e.ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim();
        await _db.SaveChangesAsync();
        await _recommender.RefreshAsync();

        return NoContent();
    }

    [HttpPost("events/{id:long}/listings")]
    public async Task<ActionResult<OrganizerEventDetailDto>> AddListing(long id, CreateListingRequest request)
    {
        var eventExists = await _db.Events.AnyAsync(e => e.EventId == id && e.CreatedByUserId == CurrentUserId);
        if (!eventExists) return NotFound();

        _db.Listings.Add(new Listing
        {
            ListingId = GenerateListingId(),
            EventId = id,
            Section = string.IsNullOrWhiteSpace(request.Section) ? "General Admission" : request.Section,
            Quantity = request.Quantity,
            QuantityRemaining = request.Quantity,
            UnitPrice = request.UnitPrice,
            ListingStatus = request.Quantity > 0 ? ListingStatus.Available : ListingStatus.SoldOut,
        });
        await _db.SaveChangesAsync();
        await _recommender.RefreshAsync();

        return await GetMyEvent(id);
    }

    [HttpPut("listings/{listingId}")]
    public async Task<ActionResult> UpdateListing(string listingId, UpdateListingRequest request)
    {
        var listing = await _db.Listings.Include(l => l.Event)
            .FirstOrDefaultAsync(l => l.ListingId == listingId && l.Event!.CreatedByUserId == CurrentUserId);
        if (listing is null) return NotFound();

        var sold = listing.Quantity - listing.QuantityRemaining;
        if (request.Quantity < sold)
        {
            return BadRequest(new { message = $"Quantity can't be lower than the {sold} tickets already sold." });
        }

        listing.Quantity = request.Quantity;
        listing.QuantityRemaining = request.Quantity - sold;
        listing.UnitPrice = request.UnitPrice;
        listing.ListingStatus = listing.QuantityRemaining > 0 ? ListingStatus.Available : ListingStatus.SoldOut;
        await _db.SaveChangesAsync();
        await _recommender.RefreshAsync();

        return NoContent();
    }

    [HttpGet("events/{id:long}/bookings")]
    public async Task<ActionResult<List<OrganizerBookingDto>>> GetEventBookings(long id)
    {
        var eventExists = await _db.Events.AnyAsync(e => e.EventId == id && e.CreatedByUserId == CurrentUserId);
        if (!eventExists) return NotFound();

        var bookings = await _db.Bookings.AsNoTracking()
            .Where(b => b.EventId == id)
            .Include(b => b.User)
            .Include(b => b.Items)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new OrganizerBookingDto(
                b.BookingId,
                b.BookingReference,
                b.User!.FullName,
                b.User.Email,
                b.Items.Sum(i => i.Quantity),
                b.TotalAmount,
                b.Status.ToString(),
                b.CreatedAt
            ))
            .ToListAsync();

        return Ok(bookings);
    }
}
