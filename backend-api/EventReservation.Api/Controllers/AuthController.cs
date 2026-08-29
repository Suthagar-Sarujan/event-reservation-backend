using System.IdentityModel.Tokens.Jwt;
using EventReservation.Application.DTOs;
using EventReservation.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventReservation.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth)
    {
        _auth = auth;
    }

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var result = await _auth.RegisterAsync(request);
        return result.Status switch
        {
            AuthStatus.EmailAlreadyExists => Conflict(new { message = "An account with this email already exists." }),
            _ => Ok(result.Response),
        };
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var result = await _auth.LoginAsync(request);
        return result.Status switch
        {
            AuthStatus.InvalidCredentials => Unauthorized(new { message = "Invalid email or password." }),
            _ => Ok(result.Response),
        };
    }

    // Always the same response regardless of whether the email matches an
    // account - AuthService.ForgotPasswordAsync is the layer that actually
    // knows which case it was and deliberately doesn't report back which.
    [HttpPost("forgot-password")]
    public async Task<ActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        await _auth.ForgotPasswordAsync(request.Email);
        return Ok(new { message = "If an account exists for this email address, password reset instructions have been sent." });
    }

    [HttpPost("reset-password")]
    public async Task<ActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var status = await _auth.ResetPasswordAsync(request.Token, request.NewPassword);
        return status switch
        {
            ResetPasswordStatus.InvalidOrExpiredToken => BadRequest(new { message = "This password reset link is invalid or has expired." }),
            _ => NoContent(),
        };
    }

    // Theme is a per-account preference (not tied to any role), so any
    // authenticated user can update their own - stored server-side so it
    // follows them across devices/browsers, not just the current one.
    [HttpPatch("theme")]
    [Authorize]
    public async Task<ActionResult<ThemeResponse>> UpdateTheme(UpdateThemeRequest request)
    {
        var (status, theme) = await _auth.UpdateThemeAsync(CurrentUserId, request.Theme);
        return status switch
        {
            ThemeUpdateStatus.InvalidTheme => BadRequest(new { message = "Theme must be 'light', 'dark', or 'system'." }),
            ThemeUpdateStatus.UserNotFound => NotFound(),
            _ => Ok(new ThemeResponse(theme!)),
        };
    }
}
