using EventReservation.Domain.Entities;
using EventReservation.Application.DTOs;
using EventReservation.Application.Repositories;
using EventReservation.Application.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace EventReservation.Api.Tests.Services;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUserPreferenceRepository> _preferences = new();
    private readonly AuthService _sut;

    private readonly Mock<IJwtTokenService> _jwt = new();

    public AuthServiceTests()
    {
        _jwt.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns("fake-jwt-token");
        _preferences.Setup(p => p.ExistsAsync(It.IsAny<int>())).ReturnsAsync(false);
        _sut = new AuthService(_users.Object, _preferences.Object, _jwt.Object);
    }

    [Fact]
    public async Task RegisterAsync_WithNewEmail_CreatesUserAndReturnsToken()
    {
        _users.Setup(r => r.EmailExistsAsync("new@example.com")).ReturnsAsync(false);
        _users.Setup(r => r.AddAsync(It.IsAny<User>()))
            .Callback<User>(u => u.UserId = 42)
            .Returns(Task.CompletedTask);

        var result = await _sut.RegisterAsync(new RegisterRequest("Jane Doe", "New@Example.com", "password123"));

        Assert.Equal(AuthStatus.Success, result.Status);
        Assert.NotNull(result.Response);
        Assert.Equal(42, result.Response!.UserId);
        Assert.Equal("new@example.com", result.Response.Email); // normalized to lowercase
        Assert.False(string.IsNullOrWhiteSpace(result.Response.Token));
        _users.Verify(r => r.AddAsync(It.Is<User>(u => u.Role == UserRole.Customer)), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_WithExistingEmail_ReturnsConflictWithoutCreatingUser()
    {
        _users.Setup(r => r.EmailExistsAsync("taken@example.com")).ReturnsAsync(true);

        var result = await _sut.RegisterAsync(new RegisterRequest("Jane Doe", "taken@example.com", "password123"));

        Assert.Equal(AuthStatus.EmailAlreadyExists, result.Status);
        Assert.Null(result.Response);
        _users.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WithCorrectPassword_ReturnsToken()
    {
        var user = new User
        {
            UserId = 7,
            FullName = "Jane Doe",
            Email = "jane@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct-password"),
            Role = UserRole.Organizer,
        };
        _users.Setup(r => r.GetByEmailAsync("jane@example.com")).ReturnsAsync(user);

        var result = await _sut.LoginAsync(new LoginRequest("jane@example.com", "correct-password"));

        Assert.Equal(AuthStatus.Success, result.Status);
        Assert.Equal("Organizer", result.Response!.Role);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsInvalidCredentials()
    {
        var user = new User
        {
            UserId = 7,
            Email = "jane@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct-password"),
        };
        _users.Setup(r => r.GetByEmailAsync("jane@example.com")).ReturnsAsync(user);

        var result = await _sut.LoginAsync(new LoginRequest("jane@example.com", "wrong-password"));

        Assert.Equal(AuthStatus.InvalidCredentials, result.Status);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task LoginAsync_WithUnknownEmail_ReturnsInvalidCredentials()
    {
        _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

        var result = await _sut.LoginAsync(new LoginRequest("nobody@example.com", "whatever"));

        Assert.Equal(AuthStatus.InvalidCredentials, result.Status);
    }
}
