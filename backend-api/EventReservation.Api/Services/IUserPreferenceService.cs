using EventReservation.Api.DTOs;

namespace EventReservation.Api.Services;

public interface IUserPreferenceService
{
    Task<UserPreferencesDto> GetAsync(int userId);
    Task<UserPreferencesDto> UpsertAsync(int userId, UpdateUserPreferencesRequest request);
}
