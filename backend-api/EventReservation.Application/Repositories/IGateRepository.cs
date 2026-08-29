using EventReservation.Domain.Entities;

namespace EventReservation.Application.Repositories;

public enum GateDeleteStatus
{
    Success,
    NotFound,
    HasHistory,
}

public interface IGateRepository
{
    /// <summary>Paged, with Assignments included so the caller can compute AssignedGateUserCount.</summary>
    Task<(int Total, List<Gate> Items)> SearchAsync(string? search, GateStatus? status, int page, int pageSize);

    Task<Gate?> GetByIdAsync(int gateId);

    /// <summary>With Assignments.User included, for the admin gate-detail view.</summary>
    Task<Gate?> GetDetailAsync(int gateId);

    Task<bool> NameExistsAsync(string name, int? excludeGateId = null);

    Task AddAsync(Gate gate);

    Task SaveChangesAsync();

    /// <summary>
    /// Physically deletes the gate only if it has zero assignments and zero
    /// scan-history rows; otherwise returns HasHistory so the admin is told to
    /// deactivate instead of losing audit data.
    /// </summary>
    Task<GateDeleteStatus> DeleteAsync(int gateId);

    /// <summary>Idempotent - a no-op if the assignment already exists.</summary>
    Task AssignUserAsync(int gateId, int userId, int? assignedByUserId);

    /// <summary>Returns false if no such assignment existed.</summary>
    Task<bool> RemoveUserAsync(int gateId, int userId);

    /// <summary>All gate ids this user is assigned to, regardless of gate status.</summary>
    Task<List<int>> GetAssignedGateIdsForUserAsync(int userId);

    /// <summary>Gates this user is assigned to AND currently Active - what a Gate User sees as "my gates".</summary>
    Task<List<Gate>> GetAssignedActiveGatesForUserAsync(int userId);

    /// <summary>Whether an assignment row exists for this (userId, gateId) pair - does NOT check the gate's status, that's the caller's job (see GateService.ScanTicketAsync).</summary>
    Task<bool> IsUserAssignedToGateAsync(int userId, int gateId);

    /// <summary>Paged Gate User accounts (Role == GateUser), with each user's assigned gate ids batched in.</summary>
    Task<(int Total, List<User> Items, Dictionary<int, List<int>> GateIdsByUser)> SearchGateUsersAsync(string? search, int page, int pageSize);
}
