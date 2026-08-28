using EventReservation.Application.DTOs;
using EventReservation.Application.Repositories;

namespace EventReservation.Application.Services;

public class EventService : IEventService
{
    private readonly IEventRepository _events;
    private readonly IRecommenderClient _recommender;

    public EventService(IEventRepository events, IRecommenderClient recommender)
    {
        _events = events;
        _recommender = recommender;
    }

    public async Task<(int Total, int Page, int PageSize, List<EventSummaryDto> Items)> SearchAsync(
        string? search, string? type, string? taxonomySubName, bool bookableOnly, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (total, items) = await _events.SearchAsync(search, type, taxonomySubName, bookableOnly, page, pageSize);
        return (total, page, pageSize, items);
    }

    public Task<(List<string> Types, List<string> SubCategories)> GetFiltersAsync() => _events.GetFiltersAsync();

    public async Task<EventDetailDto?> GetDetailAsync(long id)
    {
        var e = await _events.GetDetailAsync(id);
        if (e is null) return null;

        return new EventDetailDto(
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
    }

    public async Task<List<RecommendedEventDto>?> GetSimilarAsync(long id, int topN)
    {
        if (!await _events.ExistsAsync(id)) return null;

        var recs = await _recommender.GetSimilarEventsAsync(id, topN);
        var eventIds = recs.Items.Select(i => i.EventId).ToList();
        var summaries = await _events.GetSummariesByIdsAsync(eventIds);

        return recs.Items
            .Where(i => summaries.ContainsKey(i.EventId))
            .Select(i => new RecommendedEventDto(summaries[i.EventId], i.Score, i.Reason))
            .ToList();
    }
}
