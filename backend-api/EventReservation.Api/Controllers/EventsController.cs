using EventReservation.Application.DTOs;
using EventReservation.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace EventReservation.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly IEventService _eventService;

    public EventsController(IEventService eventService)
    {
        _eventService = eventService;
    }

    [HttpGet]
    public async Task<ActionResult> GetEvents(
        [FromQuery] string? search,
        [FromQuery] string? type,
        [FromQuery] string? taxonomySubName,
        [FromQuery] bool bookableOnly = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (total, resolvedPage, resolvedPageSize, items) =
            await _eventService.SearchAsync(search, type, taxonomySubName, bookableOnly, page, pageSize);
        return Ok(new { total, page = resolvedPage, pageSize = resolvedPageSize, items });
    }

    [HttpGet("filters")]
    public async Task<ActionResult> GetFilters()
    {
        var (types, subCategories) = await _eventService.GetFiltersAsync();
        return Ok(new { types, subCategories });
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<EventDetailDto>> GetEvent(long id)
    {
        var dto = await _eventService.GetDetailAsync(id);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpGet("{id:long}/similar")]
    public async Task<ActionResult<List<RecommendedEventDto>>> GetSimilarEvents(long id, [FromQuery] int topN = 6)
    {
        var result = await _eventService.GetSimilarAsync(id, topN);
        return result is null ? NotFound() : Ok(result);
    }
}
