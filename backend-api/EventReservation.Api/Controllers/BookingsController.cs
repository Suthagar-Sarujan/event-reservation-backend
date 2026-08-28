using System.IdentityModel.Tokens.Jwt;
using EventReservation.Application.DTOs;
using EventReservation.Application.Repositories;
using EventReservation.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CancelStatus = EventReservation.Application.Repositories.BookingCancellationStatus;

namespace EventReservation.Api.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingsController : ControllerBase
{
    private readonly IBookingService _bookingService;

    public BookingsController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet("me")]
    public async Task<ActionResult<List<BookingResponseDto>>> GetMyBookings()
    {
        var bookings = await _bookingService.GetMyBookingsAsync(CurrentUserId);
        return Ok(bookings);
    }

    [HttpPost]
    public async Task<ActionResult<BookingResponseDto>> CreateBooking(CreateBookingRequest request)
    {
        var ipAddress = ResolveClientIp();
        var (status, booking, availableQuantity) = await _bookingService.CreateBookingAsync(CurrentUserId, request, ipAddress);
        return status switch
        {
            BookingCreationStatus.ListingNotFound => NotFound(new { message = "Listing not found." }),
            BookingCreationStatus.InsufficientQuantity => BadRequest(new { message = $"Only {availableQuantity} tickets remain for this listing." }),
            BookingCreationStatus.SoldOutRace => BadRequest(new { message = "Tickets for this listing were just sold out. Please try another listing." }),
            BookingCreationStatus.TicketLimitExceeded => BadRequest(new { message = "Maximum ticket limit for this event has been reached." }),
            BookingCreationStatus.FraudBlocked => StatusCode(403, new { message = "This booking has been blocked due to unusual activity. Please contact support." }),
            _ => CreatedAtAction(nameof(GetMyBookings), null, booking),
        };
    }

    [HttpGet("{id:int}/ticket")]
    public async Task<ActionResult<TicketDto>> GetTicket(int id)
    {
        var ticket = await _bookingService.GetTicketAsync(id, CurrentUserId);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult> CancelBooking(int id)
    {
        var status = await _bookingService.CancelBookingAsync(id, CurrentUserId);
        return status switch
        {
            CancelStatus.NotFound => NotFound(),
            CancelStatus.AlreadyCancelled => BadRequest(new { message = "This booking has already been cancelled." }),
            CancelStatus.AlreadyCheckedIn => BadRequest(new { message = "This ticket has already been checked in at the event and can no longer be cancelled." }),
            CancelStatus.EventAlreadyOccurred => BadRequest(new { message = "This event has already taken place and can no longer be cancelled." }),
            _ => NoContent(),
        };
    }

    // Stored/used for fraud-detection signals only (velocity/IP-reuse scoring),
    // never exposed to the customer-facing API - see README's fraud-detection
    // section for the privacy rule this follows. X-Forwarded-For is honored
    // first for the (not currently deployed, but standard) case of running
    // behind a reverse proxy.
    private string? ResolveClientIp()
    {
        if (Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded) && forwarded.Count > 0)
        {
            var first = forwarded[0]?.Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(first)) return first;
        }
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}
