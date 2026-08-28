using EventReservation.Domain.Entities;
using EventReservation.Application.DTOs;

namespace EventReservation.Infrastructure.Persistence;

public static class EventQueryExtensions
{
    public static IQueryable<EventSummaryDto> ProjectToSummary(this IQueryable<Event> source) =>
        source.Select(e => new EventSummaryDto(
            e.EventId,
            e.Name,
            e.Type,
            e.TaxonomyName,
            e.TaxonomySubName,
            e.DatetimeUtc,
            e.Venue!.Name,
            e.Venue.AddressCity,
            e.Venue.AddressState,
            e.Listings.Where(l => l.QuantityRemaining > 0).Min(l => (decimal?)l.UnitPrice),
            e.Listings.Where(l => l.QuantityRemaining > 0).Sum(l => l.QuantityRemaining),
            e.EventPerformers.Select(ep => ep.Performer!.Name).ToList(),
            e.ImageUrl
        ));
}
