using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventReservation.Api.Services;

public record RecommendationItemDto(
    [property: JsonPropertyName("event_id")] long EventId,
    [property: JsonPropertyName("score")] double Score,
    [property: JsonPropertyName("reason")] string Reason
);

public record RecommendationResponseDto(
    [property: JsonPropertyName("items")] List<RecommendationItemDto> Items,
    [property: JsonPropertyName("personalized")] bool Personalized
);

/// <summary>
/// Thin HTTP client for the Python recommender microservice. The backend owns
/// user/booking data; the recommender only ever sees the list of event ids a
/// user has booked, never personal information, keeping the two services
/// independently deployable and retrainable as described in the project
/// architecture.
/// </summary>
public class RecommenderClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public RecommenderClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<RecommendationResponseDto> GetRecommendationsForUserAsync(IReadOnlyList<long> bookedEventIds, int topN = 10)
    {
        var response = await _http.PostAsJsonAsync("/recommendations/user", new
        {
            booked_event_ids = bookedEventIds,
            top_n = topN,
        });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RecommendationResponseDto>(JsonOptions);
        return result ?? new RecommendationResponseDto(new List<RecommendationItemDto>(), false);
    }

    public async Task<RecommendationResponseDto> GetSimilarEventsAsync(long eventId, int topN = 10)
    {
        var response = await _http.GetAsync($"/recommendations/similar/{eventId}?top_n={topN}");
        if (!response.IsSuccessStatusCode)
        {
            return new RecommendationResponseDto(new List<RecommendationItemDto>(), false);
        }
        var result = await response.Content.ReadFromJsonAsync<RecommendationResponseDto>(JsonOptions);
        return result ?? new RecommendationResponseDto(new List<RecommendationItemDto>(), false);
    }

    public async Task<RecommendationResponseDto> GetPopularEventsAsync(int topN = 10)
    {
        var response = await _http.GetAsync($"/recommendations/popular?top_n={topN}");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RecommendationResponseDto>(JsonOptions);
        return result ?? new RecommendationResponseDto(new List<RecommendationItemDto>(), false);
    }

    /// <summary>
    /// Rebuilds the recommender's in-memory feature matrix so a newly created or
    /// edited organizer event/listing shows up in recommendations immediately,
    /// instead of waiting for the next service restart. Best-effort: a failure
    /// here should never block the caller's own create/update operation.
    /// </summary>
    public async Task RefreshAsync()
    {
        try
        {
            await _http.PostAsync("/admin/refresh", null);
        }
        catch (HttpRequestException)
        {
            // Recommender being briefly unavailable shouldn't fail an organizer's edit.
        }
    }
}
