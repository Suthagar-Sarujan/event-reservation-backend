using EventReservation.Domain.Entities;
using EventReservation.Application.DTOs;
using EventReservation.Application.Repositories;
using EventReservation.Application.Services;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EventReservation.Api.Tests.Services;

public class BookingServiceTests
{
    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<IFraudDetectionService> _fraud = new();
    private readonly Mock<IQrCodeService> _qr = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly BookingService _sut;

    public BookingServiceTests()
    {
        // Default every test to a low-risk, non-blocking evaluation so booking
        // flow tests don't need to know about fraud scoring unless they're
        // specifically testing it (see FraudDetectionServiceTests /
        // BookingServiceFraudTests for that).
        _fraud.Setup(f => f.EvaluateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(new FraudEvaluation(RiskDecision.Allowed, 0, RiskLevel.Low, []));
        _email.Setup(e => e.SendBookingConfirmationAsync(It.IsAny<int>())).ReturnsAsync(EmailSendResult.Sent);
        _sut = new BookingService(_bookings.Object, _fraud.Object, _qr.Object, _email.Object, Options.Create(new FraudOptions()));
    }

    [Fact]
    public async Task CreateBookingAsync_WhenListingNotFound_ReturnsListingNotFoundWithNoBooking()
    {
        _bookings.Setup(r => r.GetEventIdForListingAsync("missing-listing")).ReturnsAsync((long?)null);

        var (status, booking, available) = await _sut.CreateBookingAsync(1, new CreateBookingRequest("missing-listing", 2), null);

        Assert.Equal(BookingCreationStatus.ListingNotFound, status);
        Assert.Null(booking);
        Assert.Null(available);
    }

    [Fact]
    public async Task CreateBookingAsync_WhenInsufficientQuantity_ReturnsAvailableQuantityForTheErrorMessage()
    {
        _bookings.Setup(r => r.GetEventIdForListingAsync("listing-1")).ReturnsAsync(100L);
        _bookings
            .Setup(r => r.CreateAsync(1, "listing-1", 10, It.IsAny<int>()))
            .ReturnsAsync(new BookingCreationResult(BookingCreationStatus.InsufficientQuantity, null, 3));

        var (status, booking, available) = await _sut.CreateBookingAsync(1, new CreateBookingRequest("listing-1", 10), null);

        Assert.Equal(BookingCreationStatus.InsufficientQuantity, status);
        Assert.Equal(3, available);
    }

    [Fact]
    public async Task CreateBookingAsync_WhenTicketLimitExceeded_ReturnsTicketLimitExceededWithNoBooking()
    {
        _bookings.Setup(r => r.GetEventIdForListingAsync("listing-1")).ReturnsAsync(100L);
        _bookings
            .Setup(r => r.CreateAsync(1, "listing-1", 2, It.IsAny<int>()))
            .ReturnsAsync(new BookingCreationResult(BookingCreationStatus.TicketLimitExceeded, null, null));

        var (status, booking, available) = await _sut.CreateBookingAsync(1, new CreateBookingRequest("listing-1", 2), null);

        Assert.Equal(BookingCreationStatus.TicketLimitExceeded, status);
        Assert.Null(booking);
        _fraud.Verify(f => f.LogAsync(1, 100L, null, null, 2, 0, RiskLevel.Low, RiskDecision.Blocked,
            It.Is<IEnumerable<string>>(r => r.Contains("ticket_limit"))), Times.Once);
    }

    [Fact]
    public async Task CreateBookingAsync_WhenFraudEvaluationBlocks_ReturnsFraudBlockedWithoutCallingRepository()
    {
        _bookings.Setup(r => r.GetEventIdForListingAsync("listing-1")).ReturnsAsync(100L);
        _fraud.Setup(f => f.EvaluateAsync(1, 2, "203.0.113.9"))
            .ReturnsAsync(new FraudEvaluation(RiskDecision.Blocked, 85, RiskLevel.High, ["high_booking_velocity"]));

        var (status, booking, available) = await _sut.CreateBookingAsync(1, new CreateBookingRequest("listing-1", 2), "203.0.113.9");

        Assert.Equal(BookingCreationStatus.FraudBlocked, status);
        Assert.Null(booking);
        _bookings.Verify(r => r.CreateAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CreateBookingAsync_OnSuccess_MapsTheBookingIncludingItsItems()
    {
        var booking = new Booking
        {
            BookingId = 5,
            BookingReference = "BKG-ABC123",
            EventId = 100,
            Status = BookingStatus.Confirmed,
            TotalAmount = 50m,
            CreatedAt = new DateTime(2026, 1, 1),
            Event = new Event { EventId = 100, Name = "Test Event", DatetimeUtc = new DateTime(2026, 6, 1) },
            Items = [new BookingItem { ListingId = "listing-1", Quantity = 2, UnitPrice = 25m, Subtotal = 50m }],
        };
        _bookings.Setup(r => r.GetEventIdForListingAsync("listing-1")).ReturnsAsync(100L);
        _bookings
            .Setup(r => r.CreateAsync(1, "listing-1", 2, It.IsAny<int>()))
            .ReturnsAsync(new BookingCreationResult(BookingCreationStatus.Success, booking, null));

        var (status, dto, _) = await _sut.CreateBookingAsync(1, new CreateBookingRequest("listing-1", 2), null);

        Assert.Equal(BookingCreationStatus.Success, status);
        Assert.NotNull(dto);
        Assert.Equal("BKG-ABC123", dto!.BookingReference);
        Assert.Equal("Test Event", dto.EventName);
        Assert.Single(dto.Items);
        Assert.Equal(50m, dto.Items[0].Subtotal);
        _fraud.Verify(f => f.LogAsync(1, 100L, 5, null, 2, 0, RiskLevel.Low, RiskDecision.Allowed, It.IsAny<IEnumerable<string>>()), Times.Once);
        _email.Verify(e => e.SendBookingConfirmationAsync(5), Times.Once);
    }

    [Fact]
    public async Task CreateBookingAsync_WhenEmailSendFails_StillReportsBookingCreationAsSuccess()
    {
        var booking = new Booking
        {
            BookingId = 5,
            BookingReference = "BKG-ABC123",
            EventId = 100,
            Status = BookingStatus.Confirmed,
            TotalAmount = 50m,
            CreatedAt = new DateTime(2026, 1, 1),
            Event = new Event { EventId = 100, Name = "Test Event", DatetimeUtc = new DateTime(2026, 6, 1) },
            Items = [new BookingItem { ListingId = "listing-1", Quantity = 2, UnitPrice = 25m, Subtotal = 50m }],
        };
        _bookings.Setup(r => r.GetEventIdForListingAsync("listing-1")).ReturnsAsync(100L);
        _bookings
            .Setup(r => r.CreateAsync(1, "listing-1", 2, It.IsAny<int>()))
            .ReturnsAsync(new BookingCreationResult(BookingCreationStatus.Success, booking, null));
        _email.Setup(e => e.SendBookingConfirmationAsync(5)).ReturnsAsync(EmailSendResult.Failed);

        var (status, dto, _) = await _sut.CreateBookingAsync(1, new CreateBookingRequest("listing-1", 2), null);

        // A failed auto-send must never turn a successful booking into a failure -
        // it's tracked on the booking row (EmailStatus) for later resend, not
        // surfaced as a booking error.
        Assert.Equal(BookingCreationStatus.Success, status);
        Assert.NotNull(dto);
    }

    [Fact]
    public async Task ResendConfirmationEmailAsync_WhenBookingIsOwnedByCaller_DelegatesToEmailService()
    {
        var booking = new Booking { BookingId = 5, BookingReference = "BKG-ABC123", UserId = 9 };
        _bookings.Setup(r => r.GetByIdForUserAsync(5, 9)).ReturnsAsync(booking);
        _email.Setup(e => e.SendBookingConfirmationAsync(5)).ReturnsAsync(EmailSendResult.Sent);

        var result = await _sut.ResendConfirmationEmailAsync(5, 9);

        Assert.Equal(EmailSendResult.Sent, result);
        _email.Verify(e => e.SendBookingConfirmationAsync(5), Times.Once);
    }

    [Fact]
    public async Task ResendConfirmationEmailAsync_WhenBookingNotFoundOrNotOwned_ReturnsBookingNotFoundWithoutCallingEmailService()
    {
        _bookings.Setup(r => r.GetByIdForUserAsync(5, 9)).ReturnsAsync((Booking?)null);

        var result = await _sut.ResendConfirmationEmailAsync(5, 9);

        Assert.Equal(EmailSendResult.BookingNotFound, result);
        _email.Verify(e => e.SendBookingConfirmationAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetMyBookingsAsync_ReturnsOneDtoPerBooking()
    {
        _bookings.Setup(r => r.GetByUserAsync(9)).ReturnsAsync(
        [
            new Booking { BookingId = 1, BookingReference = "BKG-1", Status = BookingStatus.Confirmed, Event = new Event { Name = "A" }, Items = [] },
            new Booking { BookingId = 2, BookingReference = "BKG-2", Status = BookingStatus.Cancelled, Event = new Event { Name = "B" }, Items = [] },
        ]);

        var result = await _sut.GetMyBookingsAsync(9);

        Assert.Equal(2, result.Count);
        Assert.Equal("BKG-1", result[0].BookingReference);
        Assert.Equal("Cancelled", result[1].Status);
    }

    [Fact]
    public async Task GetTicketAsync_WhenBookingNotOwnedOrMissing_ReturnsNull()
    {
        _bookings.Setup(r => r.GetByIdForUserAsync(1, 9)).ReturnsAsync((Booking?)null);

        var result = await _sut.GetTicketAsync(1, 9);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetTicketAsync_WhenFound_GeneratesAQrCodeFromTheSignedToken()
    {
        var booking = new Booking
        {
            BookingId = 5,
            BookingReference = "BKG-ABC123",
            EventId = 100,
            Status = BookingStatus.Confirmed,
            TotalAmount = 50m,
            Event = new Event { EventId = 100, Name = "Test Event", DatetimeUtc = new DateTime(2026, 6, 1), Venue = new Venue { Name = "Arena" } },
            Items = [new BookingItem { ListingId = "listing-1", Quantity = 2, UnitPrice = 25m, Subtotal = 50m }],
        };
        _bookings.Setup(r => r.GetByIdForUserAsync(5, 9)).ReturnsAsync(booking);
        _qr.Setup(q => q.GenerateToken(5, "BKG-ABC123")).Returns("5.signature");
        _qr.Setup(q => q.GeneratePngDataUri("5.signature")).Returns("data:image/png;base64,AAA");

        var ticket = await _sut.GetTicketAsync(5, 9);

        Assert.NotNull(ticket);
        Assert.Equal("data:image/png;base64,AAA", ticket!.QrCodeDataUri);
        Assert.Equal("Arena", ticket.VenueName);
        Assert.Equal(2, ticket.TotalQuantity);
    }

    [Fact]
    public async Task CancelBookingAsync_ReturnsTheRepositoryStatusDirectly()
    {
        _bookings.Setup(r => r.CancelAsync(3, 9)).ReturnsAsync(new BookingCancellationResult(BookingCancellationStatus.AlreadyCheckedIn));

        var status = await _sut.CancelBookingAsync(3, 9);

        Assert.Equal(BookingCancellationStatus.AlreadyCheckedIn, status);
    }

    [Fact]
    public async Task VerifyTicketAsync_WhenCodeHasNoMatchingBooking_ReturnsNotFound()
    {
        _bookings.Setup(r => r.GetForVerificationByReferenceAsync("BKG-NOPE")).ReturnsAsync((Booking?)null);

        var result = await _sut.VerifyTicketAsync("BKG-NOPE");

        Assert.False(result.Found);
    }

    [Fact]
    public async Task VerifyTicketAsync_WhenSignatureDoesNotMatch_RejectsWithoutRevealingAttendeeDetails()
    {
        var booking = new Booking { BookingId = 7, BookingReference = "BKG-XYZ", Status = BookingStatus.Confirmed, Event = new Event { Name = "E" }, User = new User { FullName = "Jane", Email = "j@x.com" } };
        _qr.Setup(q => q.TryReadBookingId("7.badsig", out It.Ref<int>.IsAny))
            .Returns((string _, out int id) => { id = 7; return true; });
        _bookings.Setup(r => r.GetForVerificationAsync(7)).ReturnsAsync(booking);
        _qr.Setup(q => q.TryValidateToken("7.badsig", "BKG-XYZ", out It.Ref<int>.IsAny))
            .Returns((string _, string _, out int id) => { id = 0; return false; });

        var result = await _sut.VerifyTicketAsync("7.badsig");

        Assert.True(result.Found);
        Assert.False(result.SignatureValid);
        Assert.Null(result.AttendeeName);
        Assert.Null(result.AttendeeEmail);
    }

    [Fact]
    public async Task VerifyTicketAsync_WhenValidAndNotYetCheckedIn_MarksCheckedInAndReturnsSuccess()
    {
        var booking = new Booking
        {
            BookingId = 7, BookingReference = "BKG-XYZ", Status = BookingStatus.Confirmed,
            Event = new Event { Name = "E", DatetimeUtc = new DateTime(2026, 6, 1) },
            User = new User { FullName = "Jane", Email = "j@x.com" },
            Items = [new BookingItem { Quantity = 2 }],
        };
        _qr.Setup(q => q.TryReadBookingId("7.goodsig", out It.Ref<int>.IsAny))
            .Returns((string _, out int id) => { id = 7; return true; });
        _bookings.Setup(r => r.GetForVerificationAsync(7)).ReturnsAsync(booking);
        _qr.Setup(q => q.TryValidateToken("7.goodsig", "BKG-XYZ", out It.Ref<int>.IsAny))
            .Returns((string _, string _, out int id) => { id = 7; return true; });
        _bookings.Setup(r => r.TryMarkCheckedInAsync(7)).ReturnsAsync(true);

        var result = await _sut.VerifyTicketAsync("7.goodsig");

        Assert.True(result.Found);
        Assert.True(result.SignatureValid);
        Assert.False(result.AlreadyCheckedIn);
        Assert.Equal("Jane", result.AttendeeName);
        Assert.Equal(2, result.TotalQuantity);
        _bookings.Verify(r => r.TryMarkCheckedInAsync(7), Times.Once);
    }

    [Fact]
    public async Task VerifyTicketAsync_WhenAlreadyCheckedIn_DoesNotReMarkAndSaysSo()
    {
        var booking = new Booking
        {
            BookingId = 7, BookingReference = "BKG-XYZ", Status = BookingStatus.Confirmed,
            CheckedInAt = new DateTime(2026, 6, 1, 18, 0, 0),
            Event = new Event { Name = "E" }, User = new User { FullName = "Jane", Email = "j@x.com" },
        };
        _bookings.Setup(r => r.GetForVerificationByReferenceAsync("BKG-XYZ")).ReturnsAsync(booking);

        var result = await _sut.VerifyTicketAsync("BKG-XYZ");

        Assert.True(result.AlreadyCheckedIn);
        _bookings.Verify(r => r.TryMarkCheckedInAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task VerifyTicketAsync_WhenBookingWasCancelled_RejectsEntry()
    {
        var booking = new Booking
        {
            BookingId = 7, BookingReference = "BKG-XYZ", Status = BookingStatus.Cancelled,
            Event = new Event { Name = "E" }, User = new User { FullName = "Jane", Email = "j@x.com" },
        };
        _bookings.Setup(r => r.GetForVerificationByReferenceAsync("BKG-XYZ")).ReturnsAsync(booking);

        var result = await _sut.VerifyTicketAsync("BKG-XYZ");

        Assert.Contains("cancelled", result.Message, StringComparison.OrdinalIgnoreCase);
        _bookings.Verify(r => r.TryMarkCheckedInAsync(It.IsAny<int>()), Times.Never);
    }
}
