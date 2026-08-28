using System.IdentityModel.Tokens.Jwt;
using EventReservation.Api.DTOs;
using EventReservation.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventReservation.Api.Controllers;

[ApiController]
[Route("api/recommendations")]
public class RecommendationsController : ControllerBase
{
    private readonly IRecommendationService _recommendationService;

    public RecommendationsController(IRecommendationService recommendationService)
    {
        _recommendationService = recommendationService;
    }

    /// <summary>
    /// Personalized when the caller is signed in and has at least one confirmed
    /// booking; otherwise falls back to a non-personalized popularity ranking
    /// from the recommender service - there is no pretending to personalize
    /// for a user we have no signal on.
    /// </summary>
    [HttpGet("for-you")]
    [AllowAnonymous]
    public async Task<ActionResult<List<RecommendedEventDto>>> GetForYou([FromQuery] int topN = 10)
    {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        int? userId = userIdClaim is not null && int.TryParse(userIdClaim, out var parsed) ? parsed : null;

        var recs = await _recommendationService.GetForYouAsync(userId, topN);
        return Ok(recs);
    }

    [HttpGet("popular")]
    [AllowAnonymous]
    public async Task<ActionResult<List<RecommendedEventDto>>> GetPopular([FromQuery] int topN = 10)
    {
        var recs = await _recommendationService.GetPopularAsync(topN);
        return Ok(recs);
    }
}
