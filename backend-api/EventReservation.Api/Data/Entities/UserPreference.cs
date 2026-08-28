namespace EventReservation.Api.Data.Entities;

/// <summary>
/// A customer's onboarding interest profile, captured once at signup and
/// editable afterward. Feeds RecommendationService as the "cold start" signal
/// before any booking history exists, and continues to blend with booking
/// history afterward (see RecommendationService.GetForYouAsync). EventTypes
/// and MusicGenres are comma-joined free-form labels (same convention as
/// BookingRiskAssessment.Reasons) rather than a join table - the option set is
/// small and fixed on the frontend, so a normalized table would add join
/// overhead without adding query flexibility anyone needs.
/// </summary>
public class UserPreference
{
    public int UserId { get; set; }

    // Comma-joined labels, e.g. "Music Concerts,Sports".
    public string EventTypes { get; set; } = string.Empty;

    // Comma-joined labels, e.g. "Rock,EDM".
    public string MusicGenres { get; set; } = string.Empty;

    public string? Atmosphere { get; set; }
    public string? AttendanceFrequency { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User? User { get; set; }
}
