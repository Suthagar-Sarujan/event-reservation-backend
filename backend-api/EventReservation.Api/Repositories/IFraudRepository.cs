using EventReservation.Api.Data.Entities;

namespace EventReservation.Api.Repositories;

public record FraudSummaryCounts(int BlockedToday, int FlaggedToday, int TotalAssessed);

public interface IFraudRepository
{
    Task<int> CountRecentBookingsByUserAsync(int userId, DateTime sinceUtc);

    /// <summary>Distinct user ids that produced a logged attempt from this IP since <paramref name="sinceUtc"/> - a signal for one IP driving multiple accounts.</summary>
    Task<int> CountDistinctUsersByIpAsync(string ipAddress, DateTime sinceUtc);

    /// <summary>Flagged/blocked attempts by this user or (when known) this IP since <paramref name="sinceUtc"/>.</summary>
    Task<int> CountRecentNonAllowedAsync(int userId, string? ipAddress, DateTime sinceUtc);

    Task LogAsync(BookingRiskAssessment assessment);

    /// <summary>Recent assessments for events the given organizer created, newest first.</summary>
    Task<(int Total, List<BookingRiskAssessment> Items)> GetForOrganizerEventsAsync(int organizerUserId, int page, int pageSize);

    /// <summary>Recent assessments platform-wide, newest first.</summary>
    Task<(int Total, List<BookingRiskAssessment> Items)> GetPlatformWideAsync(int page, int pageSize);

    Task<FraudSummaryCounts> GetSummaryForOrganizerAsync(int organizerUserId);
    Task<FraudSummaryCounts> GetSummaryPlatformWideAsync();
}
