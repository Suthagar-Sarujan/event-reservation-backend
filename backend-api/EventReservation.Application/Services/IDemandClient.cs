using System.Text.Json.Serialization;

namespace EventReservation.Application.Services;

public record DemandPredictionResponse(
    [property: JsonPropertyName("event_id")] long EventId,
    [property: JsonPropertyName("event_name")] string EventName,
    [property: JsonPropertyName("datetime_utc")] DateTime? DatetimeUtc,
    [property: JsonPropertyName("capacity")] int Capacity,
    [property: JsonPropertyName("current_bookings")] int CurrentBookings,
    [property: JsonPropertyName("predicted_demand")] int PredictedDemand,
    [property: JsonPropertyName("expected_occupancy")] double ExpectedOccupancy,
    [property: JsonPropertyName("demand_level")] string DemandLevel
);

public record DemandModelInfoResponse(
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("trained_at")] DateTime? TrainedAt,
    [property: JsonPropertyName("training_row_count")] int TrainingRowCount,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("mae")] double? Mae
);

public interface IDemandClient
{
    Task<List<DemandPredictionResponse>> GetPredictionsAsync(IReadOnlyList<long>? eventIds = null, bool onlyUpcoming = true);
    Task<DemandPredictionResponse?> GetPredictionAsync(long eventId);
    Task<DemandModelInfoResponse> GetModelInfoAsync();
    Task<DemandModelInfoResponse> RetrainAsync();
}
