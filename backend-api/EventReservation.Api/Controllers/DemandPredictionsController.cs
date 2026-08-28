using System.IdentityModel.Tokens.Jwt;
using EventReservation.Api.DTOs;
using EventReservation.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventReservation.Api.Controllers;

[ApiController]
[Route("api/organizer/demand-predictions")]
[Authorize(Roles = "Organizer")]
public class DemandPredictionsController : ControllerBase
{
    private readonly IDemandPredictionService _demand;

    public DemandPredictionsController(IDemandPredictionService demand)
    {
        _demand = demand;
    }

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet]
    public async Task<ActionResult<List<DemandPredictionDto>>> GetMyPredictions() =>
        Ok(await _demand.GetForOrganizerAsync(CurrentUserId));

    [HttpGet("{eventId:long}")]
    public async Task<ActionResult<DemandPredictionDto>> GetPrediction(long eventId)
    {
        var prediction = await _demand.GetForOrganizerEventAsync(eventId, CurrentUserId);
        return prediction is null ? NotFound() : Ok(prediction);
    }

    [HttpGet("model-info")]
    public async Task<ActionResult<DemandModelInfoDto>> GetModelInfo() => Ok(await _demand.GetModelInfoAsync());

    [HttpPost("retrain")]
    public async Task<ActionResult<DemandModelInfoDto>> Retrain() => Ok(await _demand.RetrainAsync());
}
