using EventReservation.Domain.Entities;

namespace EventReservation.Application.Repositories;

public interface IUserPreferenceRepository
{
    Task<UserPreference?> GetByUserIdAsync(int userId);
    Task<bool> ExistsAsync(int userId);
    Task UpsertAsync(UserPreference preference);
}
