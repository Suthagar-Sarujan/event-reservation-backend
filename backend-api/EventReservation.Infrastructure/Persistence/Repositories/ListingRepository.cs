using EventReservation.Application.Repositories;
using EventReservation.Infrastructure.Persistence;
using EventReservation.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EventReservation.Infrastructure.Persistence.Repositories;

public class ListingRepository : IListingRepository
{
    private readonly AppDbContext _db;

    public ListingRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<Listing?> GetByIdWithEventAsync(string listingId) =>
        _db.Listings.Include(l => l.Event).FirstOrDefaultAsync(l => l.ListingId == listingId);

    public Task<Listing?> GetForOrganizerUpdateAsync(string listingId, int organizerUserId) =>
        _db.Listings.Include(l => l.Event)
            .FirstOrDefaultAsync(l => l.ListingId == listingId && l.Event!.CreatedByUserId == organizerUserId);

    public async Task AddAsync(Listing listing)
    {
        _db.Listings.Add(listing);
        await _db.SaveChangesAsync();
    }

    public Task<int> DecrementInventoryAsync(string listingId, int quantity) =>
        _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE listings SET quantity_remaining = quantity_remaining - {quantity}, listing_status = IF(quantity_remaining - {quantity} <= 0, 'sold_out', 'available') WHERE listing_id = {listingId} AND quantity_remaining >= {quantity}");

    public Task SaveChangesAsync() => _db.SaveChangesAsync();
}
