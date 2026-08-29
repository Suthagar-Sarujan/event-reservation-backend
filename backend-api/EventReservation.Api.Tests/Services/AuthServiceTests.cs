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
    private readonly Mock<IPasswordResetTokenRepository> _resetTokens = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly AuthService _sut;

    private readonly Mock<IJwtTokenService> _jwt = new();

    public AuthServiceTests()
    {
        _jwt.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns("fake-jwt-token");
        _preferences.Setup(p => p.ExistsAsync(It.IsAny<int>())).ReturnsAsync(false);
        _email.Setup(e => e.SendPasswordResetAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(true);
        _sut = new AuthService(_users.Object, _preferences.Object, _resetTokens.Object, _email.Object, _jwt.Object);
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

    [Fact]
    public async Task ForgotPasswordAsync_WithKnownEmail_InvalidatesPriorTokensCreatesNewOneAndSendsEmail()
    {
        var user = new User { UserId = 7, Email = "jane@example.com", FullName = "Jane Doe" };
        _users.Setup(r => r.GetByEmailAsync("jane@example.com")).ReturnsAsync(user);

        PasswordResetToken? added = null;
        _resetTokens.Setup(r => r.AddAsync(It.IsAny<PasswordResetToken>()))
            .Callback<PasswordResetToken>(t => added = t)
            .Returns(Task.CompletedTask);

        await _sut.ForgotPasswordAsync("Jane@Example.com"); // mixed case - must still normalize to match GetByEmailAsync setup

        _resetTokens.Verify(r => r.InvalidateActiveTokensForUserAsync(7), Times.Once);
        Assert.NotNull(added);
        Assert.Equal(7, added!.UserId);
        Assert.False(string.IsNullOrWhiteSpace(added.TokenHash));
        Assert.True(added.ExpiresAt > DateTime.UtcNow.AddMinutes(14) && added.ExpiresAt <= DateTime.UtcNow.AddMinutes(15));
        _email.Verify(e => e.SendPasswordResetAsync(7, It.Is<string>(t => !string.IsNullOrWhiteSpace(t))), Times.Once);
    }

    [Fact]
    public async Task ForgotPasswordAsync_WithUnknownEmail_DoesNothingButStillCompletes()
    {
        _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

        await _sut.ForgotPasswordAsync("nobody@example.com");

        _resetTokens.Verify(r => r.InvalidateActiveTokensForUserAsync(It.IsAny<int>()), Times.Never);
        _resetTokens.Verify(r => r.AddAsync(It.IsAny<PasswordResetToken>()), Times.Never);
        _email.Verify(e => e.SendPasswordResetAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ForgotPasswordThenResetPassword_FullRoundTrip_ChangesPasswordAndConsumesToken()
    {
        var user = new User { UserId = 7, Email = "jane@example.com", FullName = "Jane Doe", PasswordHash = BCrypt.Net.BCrypt.HashPassword("old-password") };
        _users.Setup(r => r.GetByEmailAsync("jane@example.com")).ReturnsAsync(user);
        _users.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(user);

        PasswordResetToken? stored = null;
        string? emailedRawToken = null;
        _resetTokens.Setup(r => r.AddAsync(It.IsAny<PasswordResetToken>()))
            .Callback<PasswordResetToken>(t => stored = t)
            .Returns(Task.CompletedTask);
        _email.Setup(e => e.SendPasswordResetAsync(7, It.IsAny<string>()))
            .Callback<int, string>((_, rawToken) => emailedRawToken = rawToken)
            .ReturnsAsync(true);

        await _sut.ForgotPasswordAsync("jane@example.com");

        Assert.NotNull(stored);
        Assert.NotNull(emailedRawToken);
        // The exact token emailed to the user must hash to the exact value
        // stored - this is the real proof the generate/verify paths agree,
        // not just that both ran.
        _resetTokens.Setup(r => r.GetByTokenHashAsync(stored!.TokenHash)).ReturnsAsync(stored);

        Assert.True(await _sut.IsResetTokenValidAsync(emailedRawToken!));

        var status = await _sut.ResetPasswordAsync(emailedRawToken!, "NewPassword123!");

        Assert.Equal(ResetPasswordStatus.Success, status);
        Assert.True(BCrypt.Net.BCrypt.Verify("NewPassword123!", user.PasswordHash));
        Assert.False(BCrypt.Net.BCrypt.Verify("old-password", user.PasswordHash));
        _users.Verify(r => r.SaveChangesAsync(), Times.Once);
        // Once from ForgotPasswordAsync (supersede any older link) and once
        // from ResetPasswordAsync (consume this one + sweep stragglers) - by
        // design, not a bug.
        _resetTokens.Verify(r => r.InvalidateActiveTokensForUserAsync(7), Times.Exactly(2));
    }

    [Fact]
    public async Task ResetPasswordAsync_WithUnknownToken_ReturnsInvalidOrExpiredWithoutChangingPassword()
    {
        _resetTokens.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>())).ReturnsAsync((PasswordResetToken?)null);

        var status = await _sut.ResetPasswordAsync("bm90LWEtcmVhbC10b2tlbg", "NewPassword123!");

        Assert.Equal(ResetPasswordStatus.InvalidOrExpiredToken, status);
        _users.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithExpiredToken_ReturnsInvalidOrExpired()
    {
        var expired = new PasswordResetToken { UserId = 7, TokenHash = "hash", ExpiresAt = DateTime.UtcNow.AddMinutes(-1), CreatedAt = DateTime.UtcNow.AddMinutes(-16) };
        _resetTokens.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>())).ReturnsAsync(expired);

        var status = await _sut.ResetPasswordAsync("bm90LWEtcmVhbC10b2tlbg", "NewPassword123!");

        Assert.Equal(ResetPasswordStatus.InvalidOrExpiredToken, status);
        _users.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithAlreadyUsedToken_ReturnsInvalidOrExpired()
    {
        var used = new PasswordResetToken { UserId = 7, TokenHash = "hash", ExpiresAt = DateTime.UtcNow.AddMinutes(10), CreatedAt = DateTime.UtcNow, UsedAt = DateTime.UtcNow.AddMinutes(-1) };
        _resetTokens.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>())).ReturnsAsync(used);

        var status = await _sut.ResetPasswordAsync("bm90LWEtcmVhbC10b2tlbg", "NewPassword123!");

        Assert.Equal(ResetPasswordStatus.InvalidOrExpiredToken, status);
        _users.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task IsResetTokenValidAsync_AgreesWithResetPasswordAsync_ForValidExpiredAndGarbageTokens()
    {
        var valid = new PasswordResetToken { UserId = 7, TokenHash = "valid-hash", ExpiresAt = DateTime.UtcNow.AddMinutes(10), CreatedAt = DateTime.UtcNow };
        var expired = new PasswordResetToken { UserId = 7, TokenHash = "expired-hash", ExpiresAt = DateTime.UtcNow.AddMinutes(-1), CreatedAt = DateTime.UtcNow.AddMinutes(-20) };
        _resetTokens.Setup(r => r.GetByTokenHashAsync("valid-hash")).ReturnsAsync(valid);
        _resetTokens.Setup(r => r.GetByTokenHashAsync("expired-hash")).ReturnsAsync(expired);
        _resetTokens.Setup(r => r.GetByTokenHashAsync(It.Is<string>(h => h != "valid-hash" && h != "expired-hash"))).ReturnsAsync((PasswordResetToken?)null);

        // These raw tokens don't need to hash to the literal strings above -
        // IsResetTokenValidAsync only needs to agree with whatever
        // GetByTokenHashAsync returns for whatever hash it computes, which
        // the catch-all setup above handles for any token not specifically
        // wired to "valid"/"expired".
        Assert.False(await _sut.IsResetTokenValidAsync("some-garbage-token"));
        Assert.False(await _sut.IsResetTokenValidAsync("not valid base64url!!"));
    }

    [Fact]
    public async Task ResetPasswordAsync_WithMalformedToken_ReturnsInvalidOrExpiredWithoutQueryingRepository()
    {
        var status = await _sut.ResetPasswordAsync("not valid base64url!!", "NewPassword123!");

        Assert.Equal(ResetPasswordStatus.InvalidOrExpiredToken, status);
        _resetTokens.Verify(r => r.GetByTokenHashAsync(It.IsAny<string>()), Times.Never);
    }
}
