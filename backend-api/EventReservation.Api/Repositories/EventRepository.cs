using EventReservation.Api.Data;
using EventReservation.Api.Data.Entities;
using EventReservation.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EventReservation.Api.Repositories;

public class EventRepository : IEventRepository
{
    private readonly AppDbContext _db;

    public EventRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<(int Total, List<EventSummaryDto> Items)> SearchAsync(
        string? search, string? type, string? taxonomySubName, bool bookableOnly, int page, int pageSize)
    {
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

        return (total, items);
    }

    public async Task<(List<string> Types, List<string> SubCategories)> GetFiltersAsync()
    {
        var types = await _db.Events.AsNoTracking().Select(e => e.Type).Where(t => t != null).Distinct().OrderBy(t => t).ToListAsync();
        var subCategories = await _db.Events.AsNoTracking().Select(e => e.TaxonomySubName).Where(t => t != null).Distinct().OrderBy(t => t).ToListAsync();
        return (types.Cast<string>().ToList(), subCategories.Cast<string>().ToList());
    }

    public Task<Event?> GetDetailAsync(long id) =>
        _db.Events.AsNoTracking()
            .Include(ev => ev.Venue)
            .Include(ev => ev.EventPerformers).ThenInclude(ep => ep.Performer)
            .Include(ev => ev.Listings)
            .FirstOrDefaultAsync(ev => ev.EventId == id);

    public Task<bool> ExistsAsync(long id) => _db.Events.AnyAsync(e => e.EventId == id);

    public Task<Dictionary<long, EventSummaryDto>> GetSummariesByIdsAsync(IEnumerable<long> eventIds) =>
        _db.Events.AsNoTracking().Where(e => eventIds.Contains(e.EventId))
            .ProjectToSummary()
            .ToDictionaryAsync(s => s.EventId);

    public Task<List<Event>> GetByOrganizerAsync(int organizerUserId) =>
        _db.Events.AsNoTracking()
            .Where(e => e.CreatedByUserId == organizerUserId)
            .Include(e => e.Venue)
            .Include(e => e.Listings)
            .OrderByDescending(e => e.DatetimeUtc)
            .ToListAsync();

    public Task<Event?> GetOrganizerEventDetailAsync(long id, int organizerUserId) =>
        _db.Events.AsNoTracking()
            .Include(ev => ev.Venue)
            .Include(ev => ev.EventPerformers).ThenInclude(ep => ep.Performer)
            .Include(ev => ev.Listings)
            .FirstOrDefaultAsync(ev => ev.EventId == id && ev.CreatedByUserId == organizerUserId);

    public Task<Event?> GetForOrganizerUpdateAsync(long id, int organizerUserId) =>
        _db.Events.FirstOrDefaultAsync(ev => ev.EventId == id && ev.CreatedByUserId == organizerUserId);

    public Task<bool> ExistsForOrganizerAsync(long id, int organizerUserId) =>
        _db.Events.AnyAsync(e => e.EventId == id && e.CreatedByUserId == organizerUserId);

    public Task<Event?> GetForAdminUpdateAsync(long id) =>
        _db.Events.FirstOrDefaultAsync(ev => ev.EventId == id);

    public async Task AddAsync(Event newEvent)
    {
        _db.Events.Add(newEvent);
        await _db.SaveChangesAsync();
    }

    public async Task AddPerformerToEventAsync(Event newEvent, string performerName, int organizerUserId)
    {
        var performer = new Performer
        {
            Name = performerName.Trim(),
            ShortName = performerName.Trim(),
            Type = newEvent.Type,
            TaxonomyName = newEvent.TaxonomyName,
            TaxonomySubName = newEvent.TaxonomySubName,
            Score = 0,
            Popularity = 0,
            IsEvent = false,
            CreatedByUserId = organizerUserId,
        };
        _db.Performers.Add(performer);
        await _db.SaveChangesAsync(); // must persist first - EventPerformer needs the auto-assigned PerformerId
        _db.EventPerformers.Add(new EventPerformer { EventId = newEvent.EventId, PerformerId = performer.PerformerId });
    }

    public async Task<(int Total, List<Event> Items)> AdminSearchAsync(string? search, int page, int pageSize)
    {
        var query = _db.Events.AsNoTracking().Include(e => e.Venue).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(e => e.Name.Contains(search));
        }
        query = query.OrderByDescending(e => e.CreatedByUserId != null).ThenByDescending(e => e.DatetimeUtc);

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Include(e => e.Listings)
            .ToListAsync();

        return (total, items);
    }

    public Task<int> CountAsync() => _db.Events.CountAsync();

    public Task<int> CountOrganizerCreatedAsync() => _db.Events.CountAsync(e => e.CreatedByUserId != null);

    public Task SaveChangesAsync() => _db.SaveChangesAsync();
}
