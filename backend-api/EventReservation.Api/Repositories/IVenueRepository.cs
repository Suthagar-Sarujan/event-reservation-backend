using EventReservation.Api.Data.Entities;

namespace EventReservation.Api.Repositories;

public interface IVenueRepository
{
    Task<List<Venue>> ListAsync();
    Task<bool> ExistsAsync(int id);
    Task AddAsync(Venue venue);
}
