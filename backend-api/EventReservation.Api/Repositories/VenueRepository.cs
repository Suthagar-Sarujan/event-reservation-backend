using EventReservation.Api.Data;
using EventReservation.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EventReservation.Api.Repositories;

public class VenueRepository : IVenueRepository
{
    private readonly AppDbContext _db;

    public VenueRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<List<Venue>> ListAsync() =>
        _db.Venues.AsNoTracking().OrderBy(v => v.Name).ToListAsync();

    public Task<bool> ExistsAsync(int id) => _db.Venues.AnyAsync(v => v.VenueId == id);

    public async Task AddAsync(Venue venue)
    {
        _db.Venues.Add(venue);
        await _db.SaveChangesAsync();
    }
}
