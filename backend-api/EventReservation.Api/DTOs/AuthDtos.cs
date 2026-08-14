using System.ComponentModel.DataAnnotations;

namespace EventReservation.Api.DTOs;

public record RegisterRequest(
    [Required, StringLength(150, MinimumLength = 2)] string FullName,
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password
);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password
);

public record AuthResponse(string Token, int UserId, string FullName, string Email, string Role);
