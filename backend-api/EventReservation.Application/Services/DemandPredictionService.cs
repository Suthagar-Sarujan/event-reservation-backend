using EventReservation.Application.DTOs;
using EventReservation.Application.Repositories;

namespace EventReservation.Application.Services;

public class DemandPredictionService : IDemandPredictionService
{
    private readonly IEventRepository _events;
    private readonly IDemandClient _demand;

    public DemandPredictionService(IEventRepository events, IDemandClient demand)
    {
        _events = events;
        _demand = demand;
    }

    public async Task<List<DemandPredictionDto>> GetForOrganizerAsync(int organizerUserId)
    {
        var myEvents = await _events.GetByOrganizerAsync(organizerUserId);
        if (myEvents.Count == 0) return new List<DemandPredictionDto>();

        var eventIds = myEvents.Select(e => e.EventId).ToList();
        var predictions = await _demand.GetPredictionsAsync(eventIds, onlyUpcoming: false);
        return predictions.Select(ToDto).OrderByDescending(p => p.ExpectedOccupancy).ToList();
    }

    public async Task<DemandPredictionDto?> GetForOrganizerEventAsync(long eventId, int organizerUserId)
    {
        var owns = await _events.ExistsForOrganizerAsync(eventId, organizerUserId);
        if (!owns) return null;

        var prediction = await _demand.GetPredictionAsync(eventId);
        return prediction is null ? null : ToDto(prediction);
    }

    public async Task<DemandModelInfoDto> GetModelInfoAsync()
    {
        var info = await _demand.GetModelInfoAsync();
        return ToDto(info);
    }

    public async Task<DemandModelInfoDto> RetrainAsync()
    {
        var info = await _demand.RetrainAsync();
        return ToDto(info);
    }

    private static DemandPredictionDto ToDto(DemandPredictionResponse r) =>
        new(r.EventId, r.EventName, r.DatetimeUtc, r.Capacity, r.CurrentBookings, r.PredictedDemand, r.ExpectedOccupancy, r.DemandLevel);

    private static DemandModelInfoDto ToDto(DemandModelInfoResponse r) =>
        new(r.Version, r.TrainedAt, r.TrainingRowCount, r.Mode, r.Mae);
}
