using System.Net.Http.Json;
using System.Text.Json;
using EventReservation.Application.Services;

namespace EventReservation.Infrastructure.Services;

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
