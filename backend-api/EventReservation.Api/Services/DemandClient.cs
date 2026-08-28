using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventReservation.Api.Services;

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

/// <summary>
/// Thin HTTP client for the Python demand-prediction endpoints, following the
/// exact same degrade-gracefully shape as RecommenderClient - a recommender
/// outage should never break the organizer dashboard, just show it empty.
/// </summary>
public class DemandClient : IDemandClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public DemandClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<DemandPredictionResponse>> GetPredictionsAsync(IReadOnlyList<long>? eventIds = null, bool onlyUpcoming = true)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/demand/predict", new
            {
                event_ids = eventIds,
                only_upcoming = onlyUpcoming,
            });
            if (!response.IsSuccessStatusCode) return new List<DemandPredictionResponse>();
            var result = await response.Content.ReadFromJsonAsync<List<DemandPredictionResponse>>(JsonOptions);
            return result ?? new List<DemandPredictionResponse>();
        }
        catch (HttpRequestException)
        {
            return new List<DemandPredictionResponse>();
        }
    }

    public async Task<DemandPredictionResponse?> GetPredictionAsync(long eventId)
    {
        try
        {
            var response = await _http.GetAsync($"/demand/predict/{eventId}");
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<DemandPredictionResponse>(JsonOptions);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<DemandModelInfoResponse> GetModelInfoAsync()
    {
        try
        {
            var response = await _http.GetAsync("/demand/model-info");
            if (!response.IsSuccessStatusCode) return EmptyModelInfo;
            return await response.Content.ReadFromJsonAsync<DemandModelInfoResponse>(JsonOptions) ?? EmptyModelInfo;
        }
        catch (HttpRequestException)
        {
            return EmptyModelInfo;
        }
    }

    public async Task<DemandModelInfoResponse> RetrainAsync()
    {
        try
        {
            var response = await _http.PostAsync("/demand/retrain", null);
            if (!response.IsSuccessStatusCode) return EmptyModelInfo;
            return await response.Content.ReadFromJsonAsync<DemandModelInfoResponse>(JsonOptions) ?? EmptyModelInfo;
        }
        catch (HttpRequestException)
        {
            return EmptyModelInfo;
        }
    }

    private static DemandModelInfoResponse EmptyModelInfo => new(null, null, 0, "unavailable", null);
}
