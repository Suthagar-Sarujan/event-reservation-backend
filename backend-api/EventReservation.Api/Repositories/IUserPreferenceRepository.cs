using EventReservation.Api.Data.Entities;

namespace EventReservation.Api.Repositories;

public interface IUserPreferenceRepository
{
    Task<UserPreference?> GetByUserIdAsync(int userId);
    Task<bool> ExistsAsync(int userId);
    Task UpsertAsync(UserPreference preference);
}
