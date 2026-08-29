using EventReservation.Domain.Entities;
using EventReservation.Application.Repositories;
using EventReservation.Application.Services;
using Moq;
using Xunit;

namespace EventReservation.Api.Tests.Services;

public class GateServiceTests
{
    private readonly Mock<IGateRepository> _gates = new();
    private readonly Mock<IGateScanRepository> _gateScans = new();
    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<IQrCodeService> _qr = new();
    private readonly GateService _sut;

    private const int GateUserId = 42;
    private const int GateId = 1;
    private const long EventId = 100;

    public GateServiceTests()
    {
        _sut = new GateService(_gates.Object, _gateScans.Object, _bookings.Object, _qr.Object);
    }

    private static Gate ActiveGate() => new() { GateId = GateId, Name = "Gate A", Status = GateStatus.Active };

    private void SetUpAuthorizedGate()
    {
        _gates.Setup(g => g.GetByIdAsync(GateId)).ReturnsAsync(ActiveGate());
        _gates.Setup(g => g.IsUserAssignedToGateAsync(GateUserId, GateId)).ReturnsAsync(true);
    }

    [Fact]
    public async Task ScanTicketAsync_WhenGateDoesNotExist_RejectsAsUnauthorizedAndLogsFailure()
    {
        _gates.Setup(g => g.GetByIdAsync(GateId)).ReturnsAsync((Gate?)null);
        GateScanHistory? logged = null;
        _gateScans.Setup(s => s.LogAsync(It.IsAny<GateScanHistory>())).Callback<GateScanHistory>(h => logged = h).Returns(Task.CompletedTask);

        var result = await _sut.ScanTicketAsync(GateUserId, GateId, "BKG-XYZ", EventId, GateScanType.CheckIn);

        Assert.False(result.Success);
        Assert.Equal("You are not authorized to scan at this gate.", result.Message);
        Assert.Null(result.BookingReference);
        Assert.Null(result.AttendeeName);
        Assert.NotNull(logged);
        Assert.Equal(GateScanStatus.Failed, logged!.Status);
        Assert.Null(logged.BookingId);
        Assert.Equal("You are not authorized to scan at this gate.", logged.FailureReason);
        _bookings.Verify(b => b.GetForVerificationAsync(It.IsAny<int>()), Times.Never);
        _bookings.Verify(b => b.GetForVerificationByReferenceAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ScanTicketAsync_WhenGateInactive_RejectsAsUnauthorizedAndLogsFailure()
    {
        _gates.Setup(g => g.GetByIdAsync(GateId)).ReturnsAsync(new Gate { GateId = GateId, Name = "Gate A", Status = GateStatus.Inactive });
        GateScanHistory? logged = null;
        _gateScans.Setup(s => s.LogAsync(It.IsAny<GateScanHistory>())).Callback<GateScanHistory>(h => logged = h).Returns(Task.CompletedTask);

        var result = await _sut.ScanTicketAsync(GateUserId, GateId, "BKG-XYZ", EventId, GateScanType.CheckIn);

        Assert.False(result.Success);
        Assert.Equal("You are not authorized to scan at this gate.", result.Message);
        Assert.NotNull(logged);
        Assert.Equal(GateScanStatus.Failed, logged!.Status);
    }

    [Fact]
    public async Task ScanTicketAsync_WhenUserNotAssignedToGate_RejectsAsUnauthorizedAndLogsFailure()
    {
        _gates.Setup(g => g.GetByIdAsync(GateId)).ReturnsAsync(ActiveGate());
        _gates.Setup(g => g.IsUserAssignedToGateAsync(GateUserId, GateId)).ReturnsAsync(false);
        GateScanHistory? logged = null;
        _gateScans.Setup(s => s.LogAsync(It.IsAny<GateScanHistory>())).Callback<GateScanHistory>(h => logged = h).Returns(Task.CompletedTask);

        var result = await _sut.ScanTicketAsync(GateUserId, GateId, "BKG-XYZ", EventId, GateScanType.CheckIn);

        Assert.False(result.Success);
        Assert.Equal("You are not authorized to scan at this gate.", result.Message);
        Assert.NotNull(logged);
    }

    [Fact]
    public async Task ScanTicketAsync_WhenSignedCodeIsMalformed_ReturnsInvalidTicketAndLogsFailure()
    {
        SetUpAuthorizedGate();
        _qr.Setup(q => q.TryReadBookingId("garbled.token", out It.Ref<int>.IsAny))
            .Returns((string _, out int id) => { id = 0; return false; });
        GateScanHistory? logged = null;
        _gateScans.Setup(s => s.LogAsync(It.IsAny<GateScanHistory>())).Callback<GateScanHistory>(h => logged = h).Returns(Task.CompletedTask);

        var result = await _sut.ScanTicketAsync(GateUserId, GateId, "garbled.token", EventId, GateScanType.CheckIn);

        Assert.False(result.Success);
        Assert.Equal("Invalid ticket.", result.Message);
        Assert.NotNull(logged);
        Assert.Null(logged!.BookingId);
        Assert.Equal(GateScanStatus.Failed, logged.Status);
    }

    [Fact]
    public async Task ScanTicketAsync_WhenSignedTokenBookingNotFound_ReturnsTicketNotFoundAndLogsFailure()
    {
        SetUpAuthorizedGate();
        _qr.Setup(q => q.TryReadBookingId("7.sig", out It.Ref<int>.IsAny))
            .Returns((string _, out int id) => { id = 7; return true; });
        _bookings.Setup(b => b.GetForVerificationAsync(7)).ReturnsAsync((Booking?)null);
        GateScanHistory? logged = null;
        _gateScans.Setup(s => s.LogAsync(It.IsAny<GateScanHistory>())).Callback<GateScanHistory>(h => logged = h).Returns(Task.CompletedTask);

        var result = await _sut.ScanTicketAsync(GateUserId, GateId, "7.sig", EventId, GateScanType.CheckIn);

        Assert.False(result.Success);
        Assert.Equal("Ticket not found.", result.Message);
        Assert.NotNull(logged);
        Assert.Null(logged!.BookingId);
    }

    [Fact]
    public async Task ScanTicketAsync_WhenSignatureInvalid_RejectsWithoutRevealingAttendeeDetailsAndLogsFailure()
    {
        SetUpAuthorizedGate();
        var booking = new Booking
        {
            BookingId = 7, BookingReference = "BKG-XYZ", EventId = EventId, Status = BookingStatus.Confirmed,
            Event = new Event { EventId = EventId, Name = "E" }, User = new User { FullName = "Jane", Email = "j@x.com" },
        };
        _qr.Setup(q => q.TryReadBookingId("7.badsig", out It.Ref<int>.IsAny))
            .Returns((string _, out int id) => { id = 7; return true; });
        _bookings.Setup(b => b.GetForVerificationAsync(7)).ReturnsAsync(booking);
        _qr.Setup(q => q.TryValidateToken("7.badsig", "BKG-XYZ", out It.Ref<int>.IsAny))
            .Returns((string _, string _, out int id) => { id = 0; return false; });
        GateScanHistory? logged = null;
        _gateScans.Setup(s => s.LogAsync(It.IsAny<GateScanHistory>())).Callback<GateScanHistory>(h => logged = h).Returns(Task.CompletedTask);

        var result = await _sut.ScanTicketAsync(GateUserId, GateId, "7.badsig", EventId, GateScanType.CheckIn);

        Assert.False(result.Success);
        Assert.Contains("cryptographically verified", result.Message);
        Assert.Null(result.AttendeeName);
        Assert.Null(result.BookingReference);
        Assert.NotNull(logged);
        Assert.Equal(7, logged!.BookingId);
        Assert.Equal(GateScanStatus.Failed, logged.Status);
    }

    [Fact]
    public async Task ScanTicketAsync_WhenManualReferenceBookingNotFound_ReturnsTicketNotFoundAndLogsFailure()
    {
        SetUpAuthorizedGate();
        _bookings.Setup(b => b.GetForVerificationByReferenceAsync("BKG-NOPE")).ReturnsAsync((Booking?)null);
        GateScanHistory? logged = null;
        _gateScans.Setup(s => s.LogAsync(It.IsAny<GateScanHistory>())).Callback<GateScanHistory>(h => logged = h).Returns(Task.CompletedTask);

        var result = await _sut.ScanTicketAsync(GateUserId, GateId, "BKG-NOPE", EventId, GateScanType.CheckIn);

        Assert.False(result.Success);
        Assert.Equal("Ticket not found.", result.Message);
        Assert.NotNull(logged);
    }

    [Fact]
    public async Task ScanTicketAsync_WhenBookingCancelled_RejectsAndLogsFailure()
    {
        SetUpAuthorizedGate();
        var booking = new Booking
        {
            BookingId = 7, BookingReference = "BKG-XYZ", EventId = EventId, Status = BookingStatus.Cancelled,
            Event = new Event { EventId = EventId, Name = "E" }, User = new User { FullName = "Jane" }, Items = [],
        };
        _bookings.Setup(b => b.GetForVerificationByReferenceAsync("BKG-XYZ")).ReturnsAsync(booking);
        GateScanHistory? logged = null;
        _gateScans.Setup(s => s.LogAsync(It.IsAny<GateScanHistory>())).Callback<GateScanHistory>(h => logged = h).Returns(Task.CompletedTask);

        var result = await _sut.ScanTicketAsync(GateUserId, GateId, "BKG-XYZ", EventId, GateScanType.CheckIn);

        Assert.False(result.Success);
        Assert.Equal("This ticket has been cancelled.", result.Message);
        Assert.NotNull(logged);
        Assert.Equal(7, logged!.BookingId);
        _bookings.Verify(b => b.TryMarkCheckedInAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ScanTicketAsync_WhenBookingIsForADifferentEvent_RejectsAndLogsFailure()
    {
        SetUpAuthorizedGate();
        var booking = new Booking
        {
            BookingId = 7, BookingReference = "BKG-XYZ", EventId = 999, Status = BookingStatus.Confirmed,
            Event = new Event { EventId = 999, Name = "Other Event" }, User = new User { FullName = "Jane" }, Items = [],
        };
        _bookings.Setup(b => b.GetForVerificationByReferenceAsync("BKG-XYZ")).ReturnsAsync(booking);
        GateScanHistory? logged = null;
        _gateScans.Setup(s => s.LogAsync(It.IsAny<GateScanHistory>())).Callback<GateScanHistory>(h => logged = h).Returns(Task.CompletedTask);

        var result = await _sut.ScanTicketAsync(GateUserId, GateId, "BKG-XYZ", EventId, GateScanType.CheckIn);

        Assert.False(result.Success);
        Assert.Equal("This ticket is not valid for this event.", result.Message);
        Assert.NotNull(logged);
        Assert.Equal(999, logged!.EventId);
        _bookings.Verify(b => b.TryMarkCheckedInAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ScanTicketAsync_WhenAlreadyCheckedIn_RejectsAndLogsFailureWithoutReMarking()
    {
        SetUpAuthorizedGate();
        var booking = new Booking
        {
            BookingId = 7, BookingReference = "BKG-XYZ", EventId = EventId, Status = BookingStatus.Confirmed,
            CheckedInAt = new DateTime(2026, 6, 1, 18, 0, 0),
            Event = new Event { EventId = EventId, Name = "E" }, User = new User { FullName = "Jane" }, Items = [],
        };
        _bookings.Setup(b => b.GetForVerificationByReferenceAsync("BKG-XYZ")).ReturnsAsync(booking);
        GateScanHistory? logged = null;
        _gateScans.Setup(s => s.LogAsync(It.IsAny<GateScanHistory>())).Callback<GateScanHistory>(h => logged = h).Returns(Task.CompletedTask);

        var result = await _sut.ScanTicketAsync(GateUserId, GateId, "BKG-XYZ", EventId, GateScanType.CheckIn);

        Assert.False(result.Success);
        Assert.Equal("This ticket has already been used.", result.Message);
        Assert.NotNull(logged);
        _bookings.Verify(b => b.TryMarkCheckedInAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ScanTicketAsync_WhenTryMarkCheckedInLosesARace_RejectsAsAlreadyUsedAndLogsFailure()
    {
        SetUpAuthorizedGate();
        var booking = new Booking
        {
            BookingId = 7, BookingReference = "BKG-XYZ", EventId = EventId, Status = BookingStatus.Confirmed,
            Event = new Event { EventId = EventId, Name = "E" }, User = new User { FullName = "Jane" }, Items = [],
        };
        _bookings.Setup(b => b.GetForVerificationByReferenceAsync("BKG-XYZ")).ReturnsAsync(booking);
        _bookings.Setup(b => b.TryMarkCheckedInAsync(7)).ReturnsAsync(false);
        GateScanHistory? logged = null;
        _gateScans.Setup(s => s.LogAsync(It.IsAny<GateScanHistory>())).Callback<GateScanHistory>(h => logged = h).Returns(Task.CompletedTask);

        var result = await _sut.ScanTicketAsync(GateUserId, GateId, "BKG-XYZ", EventId, GateScanType.CheckIn);

        Assert.False(result.Success);
        Assert.Equal("This ticket has already been used.", result.Message);
        Assert.NotNull(logged);
        Assert.Equal(GateScanStatus.Failed, logged!.Status);
        _bookings.Verify(b => b.TryMarkCheckedInAsync(7), Times.Once);
    }

    [Fact]
    public async Task ScanTicketAsync_OnSuccess_MarksCheckedInAndLogsSuccessAndReturnsDetails()
    {
        SetUpAuthorizedGate();
        var booking = new Booking
        {
            BookingId = 7, BookingReference = "BKG-XYZ", EventId = EventId, Status = BookingStatus.Confirmed,
            Event = new Event { EventId = EventId, Name = "Concert Night" },
            User = new User { FullName = "Jane Doe", Email = "jane@x.com" },
            Items = [new BookingItem { Quantity = 2 }],
        };
        _bookings.Setup(b => b.GetForVerificationByReferenceAsync("BKG-XYZ")).ReturnsAsync(booking);
        _bookings.Setup(b => b.TryMarkCheckedInAsync(7)).ReturnsAsync(true);
        GateScanHistory? logged = null;
        _gateScans.Setup(s => s.LogAsync(It.IsAny<GateScanHistory>())).Callback<GateScanHistory>(h => logged = h).Returns(Task.CompletedTask);

        var result = await _sut.ScanTicketAsync(GateUserId, GateId, "BKG-XYZ", EventId, GateScanType.CheckIn);

        Assert.True(result.Success);
        Assert.Equal("BKG-XYZ", result.BookingReference);
        Assert.Equal("Jane Doe", result.AttendeeName);
        Assert.Equal("Concert Night", result.EventName);
        Assert.Equal(2, result.TotalQuantity);
        Assert.NotNull(result.ScannedAt);
        _bookings.Verify(b => b.TryMarkCheckedInAsync(7), Times.Once);
        Assert.NotNull(logged);
        Assert.Equal(GateScanStatus.Success, logged!.Status);
        Assert.Equal(7, logged.BookingId);
        Assert.Null(logged.FailureReason);
    }

    [Fact]
    public async Task ScanTicketAsync_CheckOut_WhenNotYetCheckedIn_RejectsAndLogsFailure()
    {
        SetUpAuthorizedGate();
        var booking = new Booking
        {
            BookingId = 7, BookingReference = "BKG-XYZ", EventId = EventId, Status = BookingStatus.Confirmed,
            Event = new Event { EventId = EventId, Name = "E" }, User = new User { FullName = "Jane" }, Items = [],
        };
        _bookings.Setup(b => b.GetForVerificationByReferenceAsync("BKG-XYZ")).ReturnsAsync(booking);
        GateScanHistory? logged = null;
        _gateScans.Setup(s => s.LogAsync(It.IsAny<GateScanHistory>())).Callback<GateScanHistory>(h => logged = h).Returns(Task.CompletedTask);

        var result = await _sut.ScanTicketAsync(GateUserId, GateId, "BKG-XYZ", EventId, GateScanType.CheckOut);

        Assert.False(result.Success);
        Assert.Equal("This ticket has not been checked in yet.", result.Message);
        Assert.NotNull(logged);
        Assert.Equal(GateScanType.CheckOut, logged!.ScanType);
        _bookings.Verify(b => b.TryMarkCheckedOutAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ScanTicketAsync_CheckOut_WhenAlreadyCheckedOut_RejectsAndLogsFailureWithoutReMarking()
    {
        SetUpAuthorizedGate();
        var booking = new Booking
        {
            BookingId = 7, BookingReference = "BKG-XYZ", EventId = EventId, Status = BookingStatus.Confirmed,
            CheckedInAt = new DateTime(2026, 6, 1, 18, 0, 0),
            CheckedOutAt = new DateTime(2026, 6, 1, 22, 0, 0),
            Event = new Event { EventId = EventId, Name = "E" }, User = new User { FullName = "Jane" }, Items = [],
        };
        _bookings.Setup(b => b.GetForVerificationByReferenceAsync("BKG-XYZ")).ReturnsAsync(booking);
        GateScanHistory? logged = null;
        _gateScans.Setup(s => s.LogAsync(It.IsAny<GateScanHistory>())).Callback<GateScanHistory>(h => logged = h).Returns(Task.CompletedTask);

        var result = await _sut.ScanTicketAsync(GateUserId, GateId, "BKG-XYZ", EventId, GateScanType.CheckOut);

        Assert.False(result.Success);
        Assert.Equal("This ticket has already been checked out.", result.Message);
        Assert.NotNull(logged);
        _bookings.Verify(b => b.TryMarkCheckedOutAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ScanTicketAsync_CheckOut_WhenTryMarkCheckedOutLosesARace_RejectsAsAlreadyCheckedOutAndLogsFailure()
    {
        SetUpAuthorizedGate();
        var booking = new Booking
        {
            BookingId = 7, BookingReference = "BKG-XYZ", EventId = EventId, Status = BookingStatus.Confirmed,
            CheckedInAt = new DateTime(2026, 6, 1, 18, 0, 0),
            Event = new Event { EventId = EventId, Name = "E" }, User = new User { FullName = "Jane" }, Items = [],
        };
        _bookings.Setup(b => b.GetForVerificationByReferenceAsync("BKG-XYZ")).ReturnsAsync(booking);
        _bookings.Setup(b => b.TryMarkCheckedOutAsync(7)).ReturnsAsync(false);
        GateScanHistory? logged = null;
        _gateScans.Setup(s => s.LogAsync(It.IsAny<GateScanHistory>())).Callback<GateScanHistory>(h => logged = h).Returns(Task.CompletedTask);

        var result = await _sut.ScanTicketAsync(GateUserId, GateId, "BKG-XYZ", EventId, GateScanType.CheckOut);

        Assert.False(result.Success);
        Assert.Equal("This ticket has already been checked out.", result.Message);
        Assert.NotNull(logged);
        Assert.Equal(GateScanStatus.Failed, logged!.Status);
        _bookings.Verify(b => b.TryMarkCheckedOutAsync(7), Times.Once);
    }

    [Fact]
    public async Task ScanTicketAsync_CheckOut_OnSuccess_MarksCheckedOutAndLogsSuccessAndReturnsDetails()
    {
        SetUpAuthorizedGate();
        var booking = new Booking
        {
            BookingId = 7, BookingReference = "BKG-XYZ", EventId = EventId, Status = BookingStatus.Confirmed,
            CheckedInAt = new DateTime(2026, 6, 1, 18, 0, 0),
            Event = new Event { EventId = EventId, Name = "Concert Night" },
            User = new User { FullName = "Jane Doe", Email = "jane@x.com" },
            Items = [new BookingItem { Quantity = 2 }],
        };
        _bookings.Setup(b => b.GetForVerificationByReferenceAsync("BKG-XYZ")).ReturnsAsync(booking);
        _bookings.Setup(b => b.TryMarkCheckedOutAsync(7)).ReturnsAsync(true);
        GateScanHistory? logged = null;
        _gateScans.Setup(s => s.LogAsync(It.IsAny<GateScanHistory>())).Callback<GateScanHistory>(h => logged = h).Returns(Task.CompletedTask);

        var result = await _sut.ScanTicketAsync(GateUserId, GateId, "BKG-XYZ", EventId, GateScanType.CheckOut);

        Assert.True(result.Success);
        Assert.Equal("Checked out successfully.", result.Message);
        Assert.Equal("BKG-XYZ", result.BookingReference);
        Assert.Equal(2, result.TotalQuantity);
        _bookings.Verify(b => b.TryMarkCheckedOutAsync(7), Times.Once);
        _bookings.Verify(b => b.TryMarkCheckedInAsync(It.IsAny<int>()), Times.Never);
        Assert.NotNull(logged);
        Assert.Equal(GateScanType.CheckOut, logged!.ScanType);
        Assert.Equal(GateScanStatus.Success, logged.Status);
    }

    [Fact]
    public async Task ScanTicketAsync_CheckOut_WhenCancelled_RejectsBeforeCheckingInOutState()
    {
        SetUpAuthorizedGate();
        var booking = new Booking
        {
            BookingId = 7, BookingReference = "BKG-XYZ", EventId = EventId, Status = BookingStatus.Cancelled,
            Event = new Event { EventId = EventId, Name = "E" }, User = new User { FullName = "Jane" }, Items = [],
        };
        _bookings.Setup(b => b.GetForVerificationByReferenceAsync("BKG-XYZ")).ReturnsAsync(booking);
        _gateScans.Setup(s => s.LogAsync(It.IsAny<GateScanHistory>())).Returns(Task.CompletedTask);

        var result = await _sut.ScanTicketAsync(GateUserId, GateId, "BKG-XYZ", EventId, GateScanType.CheckOut);

        Assert.False(result.Success);
        Assert.Equal("This ticket has been cancelled.", result.Message);
        _bookings.Verify(b => b.TryMarkCheckedOutAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetMyGatesAsync_ReturnsOnlyTheRepositorysAssignedActiveGates()
    {
        _gates.Setup(g => g.GetAssignedActiveGatesForUserAsync(GateUserId)).ReturnsAsync(
        [
            new Gate { GateId = 1, Name = "Gate A", Status = GateStatus.Active },
            new Gate { GateId = 2, Name = "Gate B", Status = GateStatus.Active },
        ]);

        var result = await _sut.GetMyGatesAsync(GateUserId);

        Assert.Equal(2, result.Count);
        Assert.Equal("Gate A", result[0].Name);
        Assert.All(result, g => Assert.Equal("Active", g.Status));
    }
}
