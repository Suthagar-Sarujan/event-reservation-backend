using System.ComponentModel.DataAnnotations;

namespace EventReservation.Application.DTOs;

public record UserPreferencesDto(
    bool HasPreferences,
    List<string> EventTypes,
    List<string> MusicGenres,
    string? Atmosphere,
    string? AttendanceFrequency
);

public record UpdateUserPreferencesRequest(
    [Required] List<string> EventTypes,
    [Required] List<string> MusicGenres,
    string? Atmosphere,
    string? AttendanceFrequency
);
