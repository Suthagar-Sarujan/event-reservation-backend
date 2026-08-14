using EventReservation.Api.Data;
using EventReservation.Api.DTOs;
using EventReservation.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventReservation.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly RecommenderClient _recommender;

    public EventsController(AppDbContext db, RecommenderClient recommender)
    {
        _db = db;
        _recommender = recommender;
    }

    [HttpGet]
    public async Task<ActionResult> GetEvents(
        [FromQuery] string? search,
        [FromQuery] string? type,
        [FromQuery] string? taxonomySubName,
        [FromQuery] bool bookableOnly = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Events.AsNoTracking().Where(e => e.Status == "normal");

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(e => e.Name.Contains(search) ||
                                      e.EventPerformers.Any(ep => ep.Performer!.Name.Contains(search)));
        }
        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(e => e.Type == type);
        }
        if (!string.IsNullOrWhiteSpace(taxonomySubName))
        {
            query = query.Where(e => e.TaxonomySubName == taxonomySubName);
        }
        if (bookableOnly)
        {
            query = query.Where(e => e.Listings.Any(l => l.QuantityRemaining > 0) && e.DatetimeUtc > DateTime.UtcNow);
        }

        query = query.OrderBy(e => e.DatetimeUtc);

        var total = await query.CountAsync();
        var items = await query.ProjectToSummary()
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("filters")]
    public async Task<ActionResult> GetFilters()
    {
        var types = await _db.Events.AsNoTracking().Select(e => e.Type).Where(t => t != null).Distinct().OrderBy(t => t).ToListAsync();
        var subCategories = await _db.Events.AsNoTracking().Select(e => e.TaxonomySubName).Where(t => t != null).Distinct().OrderBy(t => t).ToListAsync();
        return Ok(new { types, subCategories });
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<EventDetailDto>> GetEvent(long id)
    {
        var e = await _db.Events.AsNoTracking()
            .Include(ev => ev.Venue)
            .Include(ev => ev.EventPerformers).ThenInclude(ep => ep.Performer)
            .Include(ev => ev.Listings)
            .FirstOrDefaultAsync(ev => ev.EventId == id);

        if (e is null) return NotFound();

        var dto = new EventDetailDto(
            e.EventId,
            e.Name,
            e.Type,
            e.TaxonomyName,
            e.TaxonomySubName,
            e.DatetimeUtc,
            e.Venue!.Name,
            e.Venue.AddressStreet,
            e.Venue.AddressCity,
            e.Venue.AddressState,
            e.Venue.AddressCountry,
            e.Venue.Capacity,
            e.EventPerformers.Select(ep => ep.Performer!.Name).ToList(),
            e.Listings.Where(l => l.QuantityRemaining > 0)
                .OrderBy(l => l.UnitPrice)
                .Select(l => new ListingDto(l.ListingId, l.Section, l.SectionFull, l.RowLabel, l.QuantityRemaining, l.DeliveryType, l.UnitPrice))
                .ToList(),
            e.ImageUrl
        );

        return Ok(dto);
    }

    [HttpGet("{id:long}/similar")]
    public async Task<ActionResult<List<RecommendedEventDto>>> GetSimilarEvents(long id, [FromQuery] int topN = 6)
    {
        if (!await _db.Events.AnyAsync(e => e.EventId == id)) return NotFound();

        var recs = await _recommender.GetSimilarEventsAsync(id, topN);
        var eventIds = recs.Items.Select(i => i.EventId).ToList();
        var summaries = await _db.Events.AsNoTracking().Where(e => eventIds.Contains(e.EventId))
            .ProjectToSummary()
            .ToDictionaryAsync(s => s.EventId);

        var ordered = recs.Items
            .Where(i => summaries.ContainsKey(i.EventId))
            .Select(i => new RecommendedEventDto(summaries[i.EventId], i.Score, i.Reason))
            .ToList();

        return Ok(ordered);
    }
}
