namespace EventReservation.Application.Services;

using EventReservation.Domain.Entities;

public interface IJwtTokenService
{
    string GenerateToken(User user);
}
