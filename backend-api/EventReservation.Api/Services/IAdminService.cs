using EventReservation.Api.DTOs;

namespace EventReservation.Api.Services;

public interface IAdminService
{
    Task<AdminStatsDto> GetStatsAsync();
    Task<(int Total, int Page, int PageSize, List<AdminUserDto> Items)> GetUsersAsync(string? search, int page, int pageSize);
    Task<AdminRoleUpdateStatus> UpdateUserRoleAsync(int userId, int currentUserId, string role);
    Task<(int Total, int Page, int PageSize, List<AdminEventDto> Items)> GetEventsAsync(string? search, int page, int pageSize);
    Task<bool> CancelEventAsync(long id);
    Task<AdminEventUpdateStatus> UpdateEventAsync(long id, UpdateEventRequest request);
    Task<(int Total, int Page, int PageSize, List<AdminBookingDto> Items)> GetBookingsAsync(string? search, int page, int pageSize);
    Task<List<TrendPointDto>> GetBookingTrendAsync(int days);
    Task<FraudOverviewDto> GetFraudOverviewAsync();
}
