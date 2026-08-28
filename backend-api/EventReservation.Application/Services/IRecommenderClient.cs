using System.Text.Json.Serialization;

namespace EventReservation.Application.Services;

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
