using EventReservation.Domain.Entities;

namespace EventReservation.Application.Repositories;

public interface IListingRepository
{
    /// <summary>Tracked, with Event included - for inventory checks before booking.</summary>
    Task<Listing?> GetByIdWithEventAsync(string listingId);

    /// <summary>Tracked, with Event included, scoped to one organizer's own listing.</summary>
    Task<Listing?> GetForOrganizerUpdateAsync(string listingId, int organizerUserId);

    Task AddAsync(Listing listing);

    /// <summary>
    /// Row-level guard against a concurrent booking selling the same tickets twice:
    /// the UPDATE only succeeds if enough inventory still exists at the moment it
    /// runs, not just when it was first read. Returns the number of rows affected
    /// (0 = someone else claimed the remaining inventory first).
    /// </summary>
    Task<int> DecrementInventoryAsync(string listingId, int quantity);

    Task SaveChangesAsync();
}
