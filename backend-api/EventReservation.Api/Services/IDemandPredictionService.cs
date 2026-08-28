using EventReservation.Api.DTOs;

namespace EventReservation.Api.Services;

public interface IDemandPredictionService
{
    /// <summary>Predictions scoped to one organizer's own events only.</summary>
    Task<List<DemandPredictionDto>> GetForOrganizerAsync(int organizerUserId);

    /// <summary>A single prediction, only if the event belongs to this organizer.</summary>
    Task<DemandPredictionDto?> GetForOrganizerEventAsync(long eventId, int organizerUserId);

    Task<DemandModelInfoDto> GetModelInfoAsync();
    Task<DemandModelInfoDto> RetrainAsync();
}
