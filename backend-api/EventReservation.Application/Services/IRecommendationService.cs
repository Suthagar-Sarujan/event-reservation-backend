using EventReservation.Application.DTOs;

namespace EventReservation.Application.Services;

public interface IRecommendationService
{
    /// <summary>
    /// Personalized when userId is not null and has at least one confirmed
    /// booking; otherwise falls back to a non-personalized popularity ranking -
    /// there is no pretending to personalize for a user we have no signal on.
    /// </summary>
    Task<List<RecommendedEventDto>> GetForYouAsync(int? userId, int topN);

    Task<List<RecommendedEventDto>> GetPopularAsync(int topN);
}
