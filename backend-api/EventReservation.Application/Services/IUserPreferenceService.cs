using EventReservation.Application.DTOs;

namespace EventReservation.Application.Services;

public interface IUserPreferenceService
{
    Task<UserPreferencesDto> GetAsync(int userId);
    Task<UserPreferencesDto> UpsertAsync(int userId, UpdateUserPreferencesRequest request);
}
