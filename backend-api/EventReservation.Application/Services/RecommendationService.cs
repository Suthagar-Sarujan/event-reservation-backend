using EventReservation.Application.DTOs;
using EventReservation.Application.Repositories;

namespace EventReservation.Application.Services;

public class RecommendationService : IRecommendationService
{
    private readonly IEventRepository _events;
    private readonly IBookingRepository _bookings;
    private readonly IUserPreferenceRepository _preferences;
    private readonly IRecommenderClient _recommender;

    public RecommendationService(
        IEventRepository events,
        IBookingRepository bookings,
        IUserPreferenceRepository preferences,
        IRecommenderClient recommender)
    {
        _events = events;
        _bookings = bookings;
        _preferences = preferences;
        _recommender = recommender;
    }

    /// <summary>
    /// Personalization has two independent signals that both feed the same
    /// ranking: booking history (behavioral) and the onboarding questionnaire
    /// (stated preference) - see UserPreference. A user with neither gets the
    /// popularity fallback; a user with only one of the two still gets a
    /// personalized ranking built from whichever signal they have.
    /// </summary>
    public async Task<List<RecommendedEventDto>> GetForYouAsync(int? userId, int topN)
    {
        var bookedEventIds = userId is null
            ? new List<long>()
            : await _bookings.GetConfirmedEventIdsForUserAsync(userId.Value);

        List<string> eventTypes = new();
        List<string> musicGenres = new();
        if (userId is not null)
        {
            var pref = await _preferences.GetByUserIdAsync(userId.Value);
            if (pref is not null)
            {
                eventTypes = pref.EventTypes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                musicGenres = pref.MusicGenres.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            }
        }

        var recs = await _recommender.GetRecommendationsForUserAsync(bookedEventIds, eventTypes, musicGenres, topN);
        return await HydrateAsync(recs);
    }

    public async Task<List<RecommendedEventDto>> GetPopularAsync(int topN)
    {
        var recs = await _recommender.GetPopularEventsAsync(topN);
        return await HydrateAsync(recs);
    }

    private async Task<List<RecommendedEventDto>> HydrateAsync(RecommendationResponseDto recs)
    {
        var eventIds = recs.Items.Select(i => i.EventId).ToList();
        var summaries = await _events.GetSummariesByIdsAsync(eventIds);

        return recs.Items
            .Where(i => summaries.ContainsKey(i.EventId))
            .Select(i => new RecommendedEventDto(summaries[i.EventId], i.Score, i.Reason))
            .ToList();
    }
}
