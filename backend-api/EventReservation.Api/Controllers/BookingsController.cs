using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using EventReservation.Api.Data;
using EventReservation.Api.Data.Entities;
using EventReservation.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventReservation.Api.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingsController : ControllerBase
{
    private readonly AppDbContext _db;

    public BookingsController(AppDbContext db)
    {
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    private static string GenerateBookingReference()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no ambiguous chars
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[8];
        for (var i = 0; i < 8; i++) chars[i] = alphabet[bytes[i] % alphabet.Length];
        return "BKG-" + new string(chars);
    }

    [HttpGet("me")]
    public async Task<ActionResult<List<BookingResponseDto>>> GetMyBookings()
    {
        var bookings = await _db.Bookings.AsNoTracking()
            .Where(b => b.UserId == CurrentUserId)
            .Include(b => b.Event)
            .Include(b => b.Items)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        var result = bookings.Select(ToDto).ToList();
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<BookingResponseDto>> CreateBooking(CreateBookingRequest request)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();

        var listing = await _db.Listings.Include(l => l.Event)
            .FirstOrDefaultAsync(l => l.ListingId == request.ListingId);
        if (listing is null) return NotFound(new { message = "Listing not found." });
        if (listing.QuantityRemaining < request.Quantity)
        {
            return BadRequest(new { message = $"Only {listing.QuantityRemaining} tickets remain for this listing." });
        }

        // Row-level guard against a concurrent booking selling the same tickets
        // twice: the UPDATE only succeeds if enough inventory still exists at
        // the moment it runs, not just when it was first read above.
        var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE listings SET quantity_remaining = quantity_remaining - {request.Quantity}, listing_status = IF(quantity_remaining - {request.Quantity} <= 0, 'sold_out', 'available') WHERE listing_id = {listing.ListingId} AND quantity_remaining >= {request.Quantity}");
        if (rowsAffected == 0)
        {
            return BadRequest(new { message = "Tickets for this listing were just sold out. Please try another listing." });
        }

        var subtotal = listing.UnitPrice * request.Quantity;
        var booking = new Booking
        {
            BookingReference = GenerateBookingReference(),
            UserId = CurrentUserId,
            EventId = listing.EventId,
            Status = BookingStatus.Confirmed,
            TotalAmount = subtotal,
            CreatedAt = DateTime.UtcNow,
        };
        booking.Items.Add(new BookingItem
        {
            ListingId = listing.ListingId,
            Quantity = request.Quantity,
            UnitPrice = listing.UnitPrice,
            Subtotal = subtotal,
        });

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        var saved = await _db.Bookings.AsNoTracking()
            .Include(b => b.Event)
            .Include(b => b.Items)
            .FirstAsync(b => b.BookingId == booking.BookingId);

        return CreatedAtAction(nameof(GetMyBookings), null, ToDto(saved));
    }

    private static BookingResponseDto ToDto(Booking b) => new(
        b.BookingId,
        b.BookingReference,
        b.EventId,
        b.Event?.Name ?? string.Empty,
        b.Event?.DatetimeUtc ?? default,
        b.Status.ToString(),
        b.TotalAmount,
        b.CreatedAt,
        b.Items.Select(i => new BookingItemDto(i.ListingId, null, i.Quantity, i.UnitPrice, i.Subtotal)).ToList()
    );
}
