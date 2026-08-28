using System.Security.Cryptography;
using EventReservation.Api.Data;
using EventReservation.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EventReservation.Api.Repositories;

public class BookingRepository : IBookingRepository
{
    private readonly AppDbContext _db;

    public BookingRepository(AppDbContext db)
    {
        _db = db;
    }

    private static string GenerateBookingReference() => GenerateCode("BKG-");

    // No real payment gateway is integrated (see README/proposal Limitations) -
    // this is a stand-in confirmation id for the simulated payment step, drawn
    // from the same random-code generator as the booking reference itself.
    private static string GeneratePaymentReference() => GenerateCode("PAY-");

    private static string GenerateCode(string prefix)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no ambiguous chars
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[8];
        for (var i = 0; i < 8; i++) chars[i] = alphabet[bytes[i] % alphabet.Length];
        return prefix + new string(chars);
    }

    public Task<List<Booking>> GetByUserAsync(int userId) =>
        _db.Bookings.AsNoTracking()
            .Where(b => b.UserId == userId)
            .Include(b => b.Event)
            .Include(b => b.Items)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

    public Task<long?> GetEventIdForListingAsync(string listingId) =>
        _db.Listings.AsNoTracking().Where(l => l.ListingId == listingId).Select(l => (long?)l.EventId).FirstOrDefaultAsync();

    public async Task<BookingCreationResult> CreateAsync(int userId, string listingId, int quantity, int maxTicketsPerEvent)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();

        var listing = await _db.Listings.Include(l => l.Event).FirstOrDefaultAsync(l => l.ListingId == listingId);
        if (listing is null)
        {
            return new BookingCreationResult(BookingCreationStatus.ListingNotFound, null, null);
        }
        if (listing.QuantityRemaining < quantity)
        {
            return new BookingCreationResult(BookingCreationStatus.InsufficientQuantity, null, listing.QuantityRemaining);
        }

        // Row-level guard against a concurrent booking selling the same tickets
        // twice: the UPDATE only succeeds if enough inventory still exists at
        // the moment it runs, not just when it was first read above.
        var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE listings SET quantity_remaining = quantity_remaining - {quantity}, listing_status = IF(quantity_remaining - {quantity} <= 0, 'sold_out', 'available') WHERE listing_id = {listing.ListingId} AND quantity_remaining >= {quantity}");
        if (rowsAffected == 0)
        {
            return new BookingCreationResult(BookingCreationStatus.SoldOutRace, null, null);
        }

        // Same concurrency-safe pattern applied to the per-(user, event) ticket
        // cap: the upsert guarantees a row exists and (per MySQL's documented
        // behaviour for ON DUPLICATE KEY UPDATE) takes an exclusive lock on it
        // even though the update is a no-op, so two simultaneous bookings from
        // the same account for the same event can't both slip past the cap.
        // updated_at is set explicitly here rather than relied on as a DB-side
        // default - the EF migration that created this table (unlike
        // scripts/schema.sql) never gave the column one, so leaving it out of
        // the INSERT list threw "Field 'updated_at' doesn't have a default
        // value" and failed every booking.
        var now = DateTime.UtcNow;
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO user_event_ticket_counts (user_id, event_id, tickets_booked, updated_at) VALUES ({userId}, {listing.EventId}, 0, {now}) ON DUPLICATE KEY UPDATE tickets_booked = tickets_booked");
        var ticketCapRowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE user_event_ticket_counts SET tickets_booked = tickets_booked + {quantity}, updated_at = {now} WHERE user_id = {userId} AND event_id = {listing.EventId} AND tickets_booked + {quantity} <= {maxTicketsPerEvent}");
        if (ticketCapRowsAffected == 0)
        {
            // Not committing rolls back the inventory decrement above too -
            // a rejected booking must never still consume inventory.
            return new BookingCreationResult(BookingCreationStatus.TicketLimitExceeded, null, null);
        }

        var subtotal = listing.UnitPrice * quantity;
        var booking = new Booking
        {
            BookingReference = GenerateBookingReference(),
            UserId = userId,
            EventId = listing.EventId,
            Status = BookingStatus.Confirmed,
            TotalAmount = subtotal,
            CreatedAt = DateTime.UtcNow,
            PaymentReference = GeneratePaymentReference(),
        };
        booking.Items.Add(new BookingItem
        {
            ListingId = listing.ListingId,
            Quantity = quantity,
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

        return new BookingCreationResult(BookingCreationStatus.Success, saved, null);
    }

    public Task<Booking?> GetByIdForUserAsync(int bookingId, int userId) =>
        _db.Bookings.AsNoTracking()
            .Where(b => b.BookingId == bookingId && b.UserId == userId)
            .Include(b => b.Event).ThenInclude(e => e!.Venue)
            .Include(b => b.Items).ThenInclude(i => i.Listing)
            .FirstOrDefaultAsync();

    public Task<Booking?> GetForVerificationAsync(int bookingId) =>
        _db.Bookings.AsNoTracking()
            .Where(b => b.BookingId == bookingId)
            .Include(b => b.Event)
            .Include(b => b.User)
            .Include(b => b.Items)
            .FirstOrDefaultAsync();

    public Task<Booking?> GetForVerificationByReferenceAsync(string bookingReference) =>
        _db.Bookings.AsNoTracking()
            .Where(b => b.BookingReference == bookingReference)
            .Include(b => b.Event)
            .Include(b => b.User)
            .Include(b => b.Items)
            .FirstOrDefaultAsync();

    public async Task<bool> TryMarkCheckedInAsync(int bookingId)
    {
        var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE bookings SET checked_in_at = {DateTime.UtcNow} WHERE booking_id = {bookingId} AND checked_in_at IS NULL AND status = 'confirmed'");
        return rowsAffected > 0;
    }

    public async Task<BookingCancellationResult> CancelAsync(int bookingId, int userId)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();

        var booking = await _db.Bookings
            .Include(b => b.Event)
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.UserId == userId);

        if (booking is null)
        {
            return new BookingCancellationResult(BookingCancellationStatus.NotFound);
        }
        if (booking.Status == BookingStatus.Cancelled)
        {
            return new BookingCancellationResult(BookingCancellationStatus.AlreadyCancelled);
        }
        if (booking.CheckedInAt is not null)
        {
            return new BookingCancellationResult(BookingCancellationStatus.AlreadyCheckedIn);
        }
        if (booking.Event is not null && booking.Event.DatetimeUtc <= DateTime.UtcNow)
        {
            return new BookingCancellationResult(BookingCancellationStatus.EventAlreadyOccurred);
        }

        // Restores each listing's inventory and flips it back to available -
        // the mirror image of the decrement in CreateAsync, inside the same
        // kind of transaction so a failure here can't strand a half-refunded
        // booking.
        foreach (var item in booking.Items)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE listings SET quantity_remaining = quantity_remaining + {item.Quantity}, listing_status = 'available' WHERE listing_id = {item.ListingId}");
        }

        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE user_event_ticket_counts SET tickets_booked = GREATEST(tickets_booked - {booking.Items.Sum(i => i.Quantity)}, 0), updated_at = {DateTime.UtcNow} WHERE user_id = {userId} AND event_id = {booking.EventId}");

        booking.Status = BookingStatus.Cancelled;
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return new BookingCancellationResult(BookingCancellationStatus.Success);
    }

    public Task<List<Booking>> GetByEventAsync(long eventId) =>
        _db.Bookings.AsNoTracking()
            .Where(b => b.EventId == eventId)
            .Include(b => b.User)
            .Include(b => b.Items)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

    public async Task<(int Total, List<Booking> Items)> AdminSearchAsync(string? search, int page, int pageSize)
    {
        var query = _db.Bookings.AsNoTracking().Include(b => b.User).Include(b => b.Event).Include(b => b.Items).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(b =>
                b.BookingReference.Contains(search) ||
                b.User!.FullName.Contains(search) ||
                b.User.Email.Contains(search) ||
                b.Event!.Name.Contains(search));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(b => b.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();
        return (total, items);
    }

    public Task<int> CountAsync() => _db.Bookings.CountAsync();

    public async Task<decimal> SumConfirmedRevenueAsync() =>
        await _db.Bookings.Where(b => b.Status == BookingStatus.Confirmed).SumAsync(b => (decimal?)b.TotalAmount) ?? 0m;

    public Task<List<long>> GetConfirmedEventIdsForUserAsync(int userId) =>
        _db.Bookings.AsNoTracking()
            .Where(b => b.UserId == userId && b.Status == BookingStatus.Confirmed)
            .Select(b => b.EventId)
            .Distinct()
            .ToListAsync();

    public async Task<List<DailyTrendPoint>> GetDailyTrendAsync(int days)
    {
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var raw = await _db.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.Confirmed && b.CreatedAt >= since)
            .GroupBy(b => b.CreatedAt.Date)
            .Select(g => new DailyTrendPoint(g.Key, g.Count(), g.Sum(b => b.TotalAmount)))
            .ToListAsync();
        return FillDateRange(raw, since, days);
    }

    public async Task<List<DailyTrendPoint>> GetDailyTrendForOrganizerAsync(int organizerUserId, int days)
    {
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var raw = await _db.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.Confirmed && b.CreatedAt >= since && b.Event!.CreatedByUserId == organizerUserId)
            .GroupBy(b => b.CreatedAt.Date)
            .Select(g => new DailyTrendPoint(g.Key, g.Count(), g.Sum(b => b.TotalAmount)))
            .ToListAsync();
        return FillDateRange(raw, since, days);
    }

    // Trend charts read gaps as "no activity", not "no data" - a day with zero
    // confirmed bookings must still appear as a zero point, not be skipped.
    private static List<DailyTrendPoint> FillDateRange(List<DailyTrendPoint> raw, DateTime since, int days)
    {
        var byDate = raw.ToDictionary(r => r.Date);
        var result = new List<DailyTrendPoint>(days);
        for (var i = 0; i < days; i++)
        {
            var date = since.AddDays(i);
            result.Add(byDate.TryGetValue(date, out var point) ? point : new DailyTrendPoint(date, 0, 0m));
        }
        return result;
    }
}
