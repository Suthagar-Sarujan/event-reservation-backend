using EventReservation.Application.DTOs;
using EventReservation.Application.Repositories;

namespace EventReservation.Application.Services;

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

    // Gate management
    Task<(int Total, int Page, int PageSize, List<GateDto> Items)> GetGatesAsync(string? search, string? status, int page, int pageSize);
    Task<GateDetailDto?> GetGateDetailAsync(int gateId);
    Task<(GateCreationStatus Status, GateDto? Gate)> CreateGateAsync(string name, string? description, int adminUserId);
    Task<GateUpdateStatus> UpdateGateAsync(int gateId, string name, string? description);
    Task<GateStatusChangeStatus> SetGateStatusAsync(int gateId, bool active);
    Task<GateDeleteStatus> DeleteGateAsync(int gateId);
    Task<(GateUserCreationStatus Status, GateUserSummaryDto? User)> CreateGateUserAsync(string fullName, string email, string password, List<int> gateIds);
    Task<(int Total, int Page, int PageSize, List<GateUserSummaryDto> Items)> GetGateUsersAsync(string? search, int page, int pageSize);
    Task<GateUserAssignStatus> AssignGateUserAsync(int gateId, int userId, int assignedByUserId);
    Task<GateUserRemoveStatus> RemoveGateUserAsync(int gateId, int userId);
    Task<(int Total, int Page, int PageSize, List<GateScanHistoryDto> Items)> GetGateScanHistoryAsync(int? gateId, string? status, DateTime? fromUtc, DateTime? toUtc, int page, int pageSize);
}
