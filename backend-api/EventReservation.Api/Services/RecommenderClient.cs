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

public interface IRecommenderClient
{
    Task<RecommendationResponseDto> GetRecommendationsForUserAsync(
        IReadOnlyList<long> bookedEventIds,
        IReadOnlyList<string>? preferredEventTypes = null,
        IReadOnlyList<string>? preferredGenres = null,
        int topN = 10);
    Task<RecommendationResponseDto> GetSimilarEventsAsync(long eventId, int topN = 10);
    Task<RecommendationResponseDto> GetPopularEventsAsync(int topN = 10);
    Task RefreshAsync();
}

/// <summary>
/// Thin HTTP client for the Python recommender microservice. The backend owns
/// user/booking data; the recommender only ever sees the list of event ids a
/// user has booked, never personal information, keeping the two services
/// independently deployable and retrainable as described in the project
/// architecture.
/// </summary>
public class RecommenderClient : IRecommenderClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public RecommenderClient(HttpClient http)
    {
        _http = http;
    }

    public Task<RecommendationResponseDto> GetRecommendationsForUserAsync(
        IReadOnlyList<long> bookedEventIds,
        IReadOnlyList<string>? preferredEventTypes = null,
        IReadOnlyList<string>? preferredGenres = null,
        int topN = 10) =>
        SafeRequest(async () =>
        {
            var response = await _http.PostAsJsonAsync("/recommendations/user", new
            {
                booked_event_ids = bookedEventIds,
                preferred_event_types = preferredEventTypes ?? Array.Empty<string>(),
                preferred_genres = preferredGenres ?? Array.Empty<string>(),
                top_n = topN,
            });
            return response;
        });

    public Task<RecommendationResponseDto> GetSimilarEventsAsync(long eventId, int topN = 10) =>
        SafeRequest(() => _http.GetAsync($"/recommendations/similar/{eventId}?top_n={topN}"));

    public Task<RecommendationResponseDto> GetPopularEventsAsync(int topN = 10) =>
        SafeRequest(() => _http.GetAsync($"/recommendations/popular?top_n={topN}"));

    /// <summary>
    /// Every recommendation lookup degrades to an empty, non-personalized result
    /// rather than failing the caller's request - a recommender restart or blip
    /// should never take down event browsing/booking with it.
    /// </summary>
    private async Task<RecommendationResponseDto> SafeRequest(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            var response = await send();
            if (!response.IsSuccessStatusCode)
            {
                return EmptyResponse;
            }
            var result = await response.Content.ReadFromJsonAsync<RecommendationResponseDto>(JsonOptions);
            return result ?? EmptyResponse;
        }
        catch (HttpRequestException)
        {
            return EmptyResponse;
        }
    }

    private static RecommendationResponseDto EmptyResponse => new(new List<RecommendationItemDto>(), false);

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
