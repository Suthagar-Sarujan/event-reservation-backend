using System.Security.Cryptography;
using EventReservation.Domain.Entities;
using EventReservation.Application.DTOs;
using EventReservation.Application.Repositories;

namespace EventReservation.Application.Services;

public class OrganizerService : IOrganizerService
{
    private readonly IEventRepository _events;
    private readonly IVenueRepository _venues;
    private readonly IListingRepository _listings;
    private readonly IBookingRepository _bookings;
    private readonly IRecommenderClient _recommender;
    private readonly IFraudRepository _fraud;
    private readonly IEmailService _email;

    public OrganizerService(
        IEventRepository events,
        IVenueRepository venues,
        IListingRepository listings,
        IBookingRepository bookings,
        IRecommenderClient recommender,
        IFraudRepository fraud,
        IEmailService email)
    {
        _events = events;
        _venues = venues;
        _listings = listings;
        _bookings = bookings;
        _recommender = recommender;
        _fraud = fraud;
        _email = email;
    }

    private static string GenerateListingId()
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
        Span<byte> bytes = stackalloc byte[10];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[10];
        for (var i = 0; i < 10; i++) chars[i] = alphabet[bytes[i] % alphabet.Length];
        return "org-" + new string(chars);
    }

    public async Task<List<VenueOptionDto>> GetVenuesAsync()
    {
        var venues = await _venues.ListAsync();
        return venues.Select(v => new VenueOptionDto(v.VenueId, v.Name, v.AddressCity, v.AddressState)).ToList();
    }

    public async Task<List<OrganizerEventSummaryDto>> GetMyEventsAsync(int organizerUserId)
    {
        var events = await _events.GetByOrganizerAsync(organizerUserId);
        return events.Select(ToSummaryDto).ToList();
    }

    public async Task<OrganizerEventDetailDto?> GetMyEventAsync(long id, int organizerUserId)
    {
        var e = await _events.GetOrganizerEventDetailAsync(id, organizerUserId);
        return e is null ? null : ToDetailDto(e);
    }

    public async Task<OrganizerEventCreationResult> CreateEventAsync(int organizerUserId, CreateEventRequest request)
    {
        int venueId;
        if (request.VenueId is not null)
        {
            if (!await _venues.ExistsAsync(request.VenueId.Value))
            {
                return new OrganizerEventCreationResult(OrganizerEventCreationStatus.VenueNotFound, null);
            }
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
                CreatedByUserId = organizerUserId,
            };
            await _venues.AddAsync(venue);
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
            CreatedByUserId = organizerUserId,
            ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim(),
        };
        await _events.AddAsync(newEvent);

        if (!string.IsNullOrWhiteSpace(request.PerformerName))
        {
            await _events.AddPerformerToEventAsync(newEvent, request.PerformerName, organizerUserId);
        }

        foreach (var listingRequest in request.Listings)
        {
            await _listings.AddAsync(new Listing
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
        await _events.SaveChangesAsync();

        await _recommender.RefreshAsync();

        var created = await GetMyEventAsync(newEvent.EventId, organizerUserId);
        return new OrganizerEventCreationResult(OrganizerEventCreationStatus.Success, created);
    }

    public async Task<OrganizerUpdateStatus> UpdateEventAsync(long id, int organizerUserId, UpdateEventRequest request)
    {
        var e = await _events.GetForOrganizerUpdateAsync(id, organizerUserId);
        if (e is null) return OrganizerUpdateStatus.NotFound;

        if (request.Status is not ("normal" or "cancelled"))
        {
            return OrganizerUpdateStatus.InvalidStatus;
        }

        e.Name = request.Name;
        e.DatetimeUtc = request.DatetimeUtc;
        e.Status = request.Status;
        e.ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim();
        await _events.SaveChangesAsync();
        await _recommender.RefreshAsync();

        return OrganizerUpdateStatus.Success;
    }

    public async Task<OrganizerEventDetailDto?> AddListingAsync(long eventId, int organizerUserId, CreateListingRequest request)
    {
        if (!await _events.ExistsForOrganizerAsync(eventId, organizerUserId)) return null;

        await _listings.AddAsync(new Listing
        {
            ListingId = GenerateListingId(),
            EventId = eventId,
            Section = string.IsNullOrWhiteSpace(request.Section) ? "General Admission" : request.Section,
            Quantity = request.Quantity,
            QuantityRemaining = request.Quantity,
            UnitPrice = request.UnitPrice,
            ListingStatus = request.Quantity > 0 ? ListingStatus.Available : ListingStatus.SoldOut,
        });
        await _recommender.RefreshAsync();

        return await GetMyEventAsync(eventId, organizerUserId);
    }

    public async Task<OrganizerListingUpdateResult> UpdateListingAsync(string listingId, int organizerUserId, UpdateListingRequest request)
    {
        var listing = await _listings.GetForOrganizerUpdateAsync(listingId, organizerUserId);
        if (listing is null) return new OrganizerListingUpdateResult(OrganizerListingUpdateStatus.NotFound, null);

        var sold = listing.Quantity - listing.QuantityRemaining;
        if (request.Quantity < sold)
        {
            return new OrganizerListingUpdateResult(OrganizerListingUpdateStatus.QuantityBelowSold, sold);
        }

        listing.Quantity = request.Quantity;
        listing.QuantityRemaining = request.Quantity - sold;
        listing.UnitPrice = request.UnitPrice;
        listing.ListingStatus = listing.QuantityRemaining > 0 ? ListingStatus.Available : ListingStatus.SoldOut;
        await _listings.SaveChangesAsync();
        await _recommender.RefreshAsync();

        return new OrganizerListingUpdateResult(OrganizerListingUpdateStatus.Success, null);
    }

    public async Task<List<OrganizerBookingDto>?> GetEventBookingsAsync(long eventId, int organizerUserId)
    {
        if (!await _events.ExistsForOrganizerAsync(eventId, organizerUserId)) return null;

        var bookings = await _bookings.GetByEventAsync(eventId);
        return bookings.Select(b => new OrganizerBookingDto(
            b.BookingId,
            b.BookingReference,
            b.User!.FullName,
            b.User.Email,
            b.Items.Sum(i => i.Quantity),
            b.TotalAmount,
            b.Status.ToString(),
            b.CreatedAt,
            b.EmailStatus.ToString(),
            b.EmailSentAt
        )).ToList();
    }

    // Scoped so an organizer can only resend email for bookings on events
    // they created - never any booking platform-wide. BookingNotFound is
    // reused for "not on your event" too, matching the ownership scoping
    // already applied to GetEventBookingsAsync.
    public async Task<EmailSendResult> ResendBookingEmailAsync(int bookingId, int organizerUserId)
    {
        var onOwnEvent = await _bookings.IsBookingOnOrganizerEventAsync(bookingId, organizerUserId);
        if (!onOwnEvent) return EmailSendResult.BookingNotFound;

        return await _email.SendBookingConfirmationAsync(bookingId);
    }

    public async Task<List<TrendPointDto>> GetSalesTrendAsync(int organizerUserId, int days)
    {
        days = Math.Clamp(days, 1, 90);
        var points = await _bookings.GetDailyTrendForOrganizerAsync(organizerUserId, days);
        return points.Select(p => new TrendPointDto(p.Date, p.Bookings, p.Revenue)).ToList();
    }

    public async Task<FraudOverviewDto> GetFraudOverviewAsync(int organizerUserId)
    {
        var summary = await _fraud.GetSummaryForOrganizerAsync(organizerUserId);
        var (_, items) = await _fraud.GetForOrganizerEventsAsync(organizerUserId, 1, 50);
        return new FraudOverviewDto(summary.BlockedToday, summary.FlaggedToday, summary.TotalAssessed, items.Select(ToRiskDto).ToList());
    }

    private static RiskAssessmentDto ToRiskDto(BookingRiskAssessment a) => new(
        a.BookingRiskId,
        a.UserId,
        a.User?.Email ?? "",
        a.EventId,
        a.Event?.Name ?? "",
        a.BookingId,
        a.IpAddress,
        a.RequestedQuantity,
        a.RiskScore,
        a.RiskLevel.ToString(),
        a.Decision.ToString(),
        a.Reasons,
        a.CreatedAt
    );

    private static OrganizerEventSummaryDto ToSummaryDto(Event e) => new(
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
    );

    private static OrganizerEventDetailDto ToDetailDto(Event e) => new(
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
}
