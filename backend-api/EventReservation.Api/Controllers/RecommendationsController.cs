using System.IdentityModel.Tokens.Jwt;
using EventReservation.Api.Data;
using EventReservation.Api.DTOs;
using EventReservation.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventReservation.Api.Controllers;

[ApiController]
[Route("api/recommendations")]
public class RecommendationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly RecommenderClient _recommender;

    public RecommendationsController(AppDbContext db, RecommenderClient recommender)
    {
        _db = db;
        _recommender = recommender;
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
        List<long> bookedEventIds = new();
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (userIdClaim is not null && int.TryParse(userIdClaim, out var userId))
        {
            bookedEventIds = await _db.Bookings.AsNoTracking()
                .Where(b => b.UserId == userId && b.Status == Data.Entities.BookingStatus.Confirmed)
                .Select(b => b.EventId)
                .Distinct()
                .ToListAsync();
        }

        var recs = await _recommender.GetRecommendationsForUserAsync(bookedEventIds, topN);
        return Ok(await Hydrate(recs));
    }

    [HttpGet("popular")]
    [AllowAnonymous]
    public async Task<ActionResult<List<RecommendedEventDto>>> GetPopular([FromQuery] int topN = 10)
    {
        var recs = await _recommender.GetPopularEventsAsync(topN);
        return Ok(await Hydrate(recs));
    }

    private async Task<List<RecommendedEventDto>> Hydrate(RecommendationResponseDto recs)
    {
        var eventIds = recs.Items.Select(i => i.EventId).ToList();
        var summaries = await _db.Events.AsNoTracking().Where(e => eventIds.Contains(e.EventId))
            .ProjectToSummary()
            .ToDictionaryAsync(s => s.EventId);

        return recs.Items
            .Where(i => summaries.ContainsKey(i.EventId))
            .Select(i => new RecommendedEventDto(summaries[i.EventId], i.Score, i.Reason))
            .ToList();
    }
}
