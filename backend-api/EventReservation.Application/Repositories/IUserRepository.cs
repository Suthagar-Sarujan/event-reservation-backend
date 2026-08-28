using EventReservation.Domain.Entities;

namespace EventReservation.Application.Repositories;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByIdAsync(int id);
    Task<bool> EmailExistsAsync(string email);
    Task AddAsync(User user);
    Task<(int Total, List<User> Items)> SearchAsync(string? search, int page, int pageSize);
    Task<Dictionary<int, string>> GetEmailsByIdsAsync(IEnumerable<int> userIds);
    Task<int> CountAsync();
    Task<int> CountByRoleAsync(UserRole role);
    Task SaveChangesAsync();
}
