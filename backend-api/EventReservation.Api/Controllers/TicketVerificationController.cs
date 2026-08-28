using EventReservation.Application.DTOs;
using EventReservation.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventReservation.Api.Controllers;

/// <summary>
/// Door-side ticket verification, usable by organizer or admin staff. Not
/// scoped to "an organizer's own events" - the free SeatGeek-imported catalogue
/// that makes up most of this platform's events has no organizer owner at all
/// (see Event.CreatedByUserId), so restricting verification to owned events
/// would make the vast majority of tickets unverifiable by anyone.
/// </summary>
[ApiController]
[Route("api/ticket-verification")]
[Authorize(Roles = "Organizer,Admin")]
public class TicketVerificationController : ControllerBase
{
    private readonly IBookingService _bookingService;

    public TicketVerificationController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    [HttpPost]
    public async Task<ActionResult<VerifyTicketResultDto>> Verify(VerifyTicketRequest request)
    {
        var result = await _bookingService.VerifyTicketAsync(request.Code);
        return Ok(result);
    }
}
