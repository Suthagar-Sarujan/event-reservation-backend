using EventReservation.Domain.Entities;

namespace EventReservation.Application.Repositories;

public interface IVenueRepository
{
    Task<List<Venue>> ListAsync();
    Task<bool> ExistsAsync(int id);
    Task AddAsync(Venue venue);
}
