using EventReservation.Api.Data.Entities;
using EventReservation.Api.DTOs;
using EventReservation.Api.Repositories;

namespace EventReservation.Api.Services;

/// <summary>
/// Backs the onboarding questionnaire (new-user interest capture) and its
/// later edits. The option lists mirror what the frontend questionnaire
/// presents - kept here too so a request can't smuggle in an arbitrary label
/// that would never match anything in RecommendationService's preference
/// matching, and so both sides of the contract are documented in one place.
/// </summary>
public class UserPreferenceService : IUserPreferenceService
{
    public static readonly IReadOnlyList<string> AllowedEventTypes = new[]
    {
        "Music Concerts", "Sports", "Comedy", "Theatre", "Cultural Events", "Festivals", "Other",
    };

    public static readonly IReadOnlyList<string> AllowedMusicGenres = new[]
    {
        "Rock", "Pop", "Hip-Hop", "EDM", "Classical", "Jazz", "R&B", "Sinhala", "Tamil", "Other",
    };

    public static readonly IReadOnlyList<string> AllowedAtmospheres = new[]
    {
        "Large concerts", "Small/independent concerts", "Outdoor festivals", "Indoor concerts",
    };

    public static readonly IReadOnlyList<string> AllowedFrequencies = new[] { "Frequently", "Occasionally", "Rarely" };

    private readonly IUserPreferenceRepository _repo;

    public UserPreferenceService(IUserPreferenceRepository repo)
    {
        _repo = repo;
    }

    public async Task<UserPreferencesDto> GetAsync(int userId)
    {
        var pref = await _repo.GetByUserIdAsync(userId);
        return ToDto(pref);
    }

    public async Task<UserPreferencesDto> UpsertAsync(int userId, UpdateUserPreferencesRequest request)
    {
        var eventTypes = Sanitize(request.EventTypes, AllowedEventTypes);
        var musicGenres = Sanitize(request.MusicGenres, AllowedMusicGenres);
        var atmosphere = request.Atmosphere is not null && AllowedAtmospheres.Contains(request.Atmosphere) ? request.Atmosphere : null;
        var frequency = request.AttendanceFrequency is not null && AllowedFrequencies.Contains(request.AttendanceFrequency) ? request.AttendanceFrequency : null;

        await _repo.UpsertAsync(new UserPreference
        {
            UserId = userId,
            EventTypes = string.Join(",", eventTypes),
            MusicGenres = string.Join(",", musicGenres),
            Atmosphere = atmosphere,
            AttendanceFrequency = frequency,
        });

        return await GetAsync(userId);
    }

    private static List<string> Sanitize(List<string> requested, IReadOnlyList<string> allowed) =>
        requested.Where(allowed.Contains).Distinct().ToList();

    private static UserPreferencesDto ToDto(UserPreference? pref)
    {
        if (pref is null)
        {
            return new UserPreferencesDto(false, new List<string>(), new List<string>(), null, null);
        }

        var eventTypes = pref.EventTypes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        var musicGenres = pref.MusicGenres.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        return new UserPreferencesDto(true, eventTypes, musicGenres, pref.Atmosphere, pref.AttendanceFrequency);
    }
}
