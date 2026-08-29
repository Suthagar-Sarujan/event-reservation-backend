using System.ComponentModel.DataAnnotations;

namespace EventReservation.Application.DTOs;

public record GateDto(int GateId, string Name, string? Description, string Status, int AssignedGateUserCount, DateTime CreatedAt, DateTime UpdatedAt);

public record GateDetailDto(int GateId, string Name, string? Description, string Status, DateTime CreatedAt, DateTime UpdatedAt, List<GateUserSummaryDto> AssignedUsers);

public record GateUserSummaryDto(int UserId, string FullName, string Email, List<int> GateIds);

public record CreateGateRequest([Required] string Name, string? Description);

public record UpdateGateRequest([Required] string Name, string? Description);

public record CreateGateUserRequest([Required] string FullName, [Required][EmailAddress] string Email, [Required] string Password, List<int> GateIds);

public record AssignGateUserRequest([Required] int UserId);
