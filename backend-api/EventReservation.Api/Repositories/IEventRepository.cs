using EventReservation.Api.Data.Entities;
using EventReservation.Api.DTOs;

namespace EventReservation.Api.Repositories;

public interface IEventRepository
{
    Task<(int Total, List<EventSummaryDto> Items)> SearchAsync(
        string? search, string? type, string? taxonomySubName, bool bookableOnly, int page, int pageSize);

    Task<(List<string> Types, List<string> SubCategories)> GetFiltersAsync();

    /// <summary>Untracked, with Venue/Performers/Listings included - for read-only detail views.</summary>
    Task<Event?> GetDetailAsync(long id);

    Task<bool> ExistsAsync(long id);

    Task<Dictionary<long, EventSummaryDto>> GetSummariesByIdsAsync(IEnumerable<long> eventIds);

    /// <summary>Untracked, with Venue/Listings included - for an organizer's own event list.</summary>
    Task<List<Event>> GetByOrganizerAsync(int organizerUserId);

    /// <summary>Untracked, with Venue/Performers/Listings included, scoped to one organizer's own event.</summary>
    Task<Event?> GetOrganizerEventDetailAsync(long id, int organizerUserId);

    /// <summary>Tracked, no includes - for updating core fields on an organizer's own event.</summary>
    Task<Event?> GetForOrganizerUpdateAsync(long id, int organizerUserId);

    Task<bool> ExistsForOrganizerAsync(long id, int organizerUserId);

    /// <summary>Tracked, no includes - admin can update/cancel any event regardless of owner.</summary>
    Task<Event?> GetForAdminUpdateAsync(long id);

    Task AddAsync(Event newEvent);

    /// <summary>Creates a Performer (organizer-supplied) and links it to the given event.</summary>
    Task AddPerformerToEventAsync(Event newEvent, string performerName, int organizerUserId);

    Task<(int Total, List<Event> Items)> AdminSearchAsync(string? search, int page, int pageSize);

    Task<int> CountAsync();
    Task<int> CountOrganizerCreatedAsync();

    Task SaveChangesAsync();
}
