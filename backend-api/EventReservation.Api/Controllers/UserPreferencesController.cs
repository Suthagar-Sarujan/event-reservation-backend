using System.IdentityModel.Tokens.Jwt;
using EventReservation.Api.DTOs;
using EventReservation.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventReservation.Api.Controllers;

[ApiController]
[Route("api/user-preferences")]
[Authorize]
public class UserPreferencesController : ControllerBase
{
    private readonly IUserPreferenceService _preferences;

    public UserPreferencesController(IUserPreferenceService preferences)
    {
        _preferences = preferences;
    }

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet]
    public async Task<ActionResult<UserPreferencesDto>> Get() => Ok(await _preferences.GetAsync(CurrentUserId));

    [HttpPut]
    public async Task<ActionResult<UserPreferencesDto>> Update(UpdateUserPreferencesRequest request) =>
        Ok(await _preferences.UpsertAsync(CurrentUserId, request));
}
