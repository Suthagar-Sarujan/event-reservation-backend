using EventReservation.Domain.Entities;
using EventReservation.Application.DTOs;
using EventReservation.Application.Repositories;
using EventReservation.Application.Services;
using Moq;
using Xunit;

namespace EventReservation.Api.Tests.Services;

public class AdminServiceTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IEventRepository> _events = new();
    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<IRecommenderClient> _recommender = new();
    private readonly Mock<IFraudRepository> _fraud = new();
    private readonly Mock<IGateRepository> _gates = new();
    private readonly Mock<IGateScanRepository> _gateScans = new();
    private readonly AdminService _sut;

    public AdminServiceTests()
    {
        _sut = new AdminService(_users.Object, _events.Object, _bookings.Object, _recommender.Object, _fraud.Object, _gates.Object, _gateScans.Object);
    }

    [Fact]
    public async Task GetStatsAsync_ComputesImportedEventsAsTotalMinusOrganizerCreated()
    {
        _users.Setup(r => r.CountAsync()).ReturnsAsync(5);
        _users.Setup(r => r.CountByRoleAsync(UserRole.Customer)).ReturnsAsync(3);
        _users.Setup(r => r.CountByRoleAsync(UserRole.Organizer)).ReturnsAsync(1);
        _users.Setup(r => r.CountByRoleAsync(UserRole.Admin)).ReturnsAsync(1);
        _events.Setup(r => r.CountAsync()).ReturnsAsync(1377);
        _events.Setup(r => r.CountOrganizerCreatedAsync()).ReturnsAsync(2);
        _bookings.Setup(r => r.CountAsync()).ReturnsAsync(10);
        _bookings.Setup(r => r.SumConfirmedRevenueAsync()).ReturnsAsync(500m);

        var stats = await _sut.GetStatsAsync();

        Assert.Equal(1377, stats.TotalEvents);
        Assert.Equal(2, stats.TotalOrganizerEvents);
        Assert.Equal(1375, stats.TotalImportedEvents);
        Assert.Equal(500m, stats.TotalRevenue);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("Organizer")]
    [InlineData("ADMIN")]
    public async Task UpdateUserRoleAsync_AcceptsRoleNamesCaseInsensitively(string role)
    {
        var user = new User { UserId = 1, Role = UserRole.Customer };
        _users.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);

        var status = await _sut.UpdateUserRoleAsync(1, currentUserId: 999, role);

        Assert.Equal(AdminRoleUpdateStatus.Success, status);
        Assert.Equal(role, user.Role.ToString(), ignoreCase: true);
    }

    [Fact]
    public async Task UpdateUserRoleAsync_WithUnrecognizedRoleName_ReturnsInvalidRole()
    {
        var status = await _sut.UpdateUserRoleAsync(1, currentUserId: 999, "superuser");

        Assert.Equal(AdminRoleUpdateStatus.InvalidRole, status);
        _users.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpdateUserRoleAsync_AdminDemotingThemself_IsRejected()
    {
        var status = await _sut.UpdateUserRoleAsync(1, currentUserId: 1, "customer");

        Assert.Equal(AdminRoleUpdateStatus.CantRemoveOwnAdmin, status);
        _users.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpdateUserRoleAsync_AdminReaffirmingOwnAdminRole_IsAllowed()
    {
        var user = new User { UserId = 1, Role = UserRole.Admin };
        _users.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);

        // Same user id, but the target role IS admin - not actually removing their own access.
        var status = await _sut.UpdateUserRoleAsync(1, currentUserId: 1, "admin");

        Assert.Equal(AdminRoleUpdateStatus.Success, status);
    }

    [Fact]
    public async Task UpdateUserRoleAsync_WhenUserDoesNotExist_ReturnsUserNotFound()
    {
        _users.Setup(r => r.GetByIdAsync(404)).ReturnsAsync((User?)null);

        var status = await _sut.UpdateUserRoleAsync(404, currentUserId: 1, "organizer");

        Assert.Equal(AdminRoleUpdateStatus.UserNotFound, status);
    }

    [Fact]
    public async Task CancelEventAsync_WhenEventDoesNotExist_ReturnsFalseWithoutRefreshingRecommender()
    {
        _events.Setup(r => r.GetForAdminUpdateAsync(404)).ReturnsAsync((Event?)null);

        var found = await _sut.CancelEventAsync(404);

        Assert.False(found);
        _recommender.Verify(r => r.RefreshAsync(), Times.Never);
    }

    [Fact]
    public async Task CancelEventAsync_OnAnyEventRegardlessOfOwner_SetsStatusCancelled()
    {
        // Admin can cancel imported (CreatedByUserId == null) events too, unlike Organizer.
        var seatgeekEvent = new Event { EventId = 1, CreatedByUserId = null, Status = "normal" };
        _events.Setup(r => r.GetForAdminUpdateAsync(1)).ReturnsAsync(seatgeekEvent);

        var found = await _sut.CancelEventAsync(1);

        Assert.True(found);
        Assert.Equal("cancelled", seatgeekEvent.Status);
        _recommender.Verify(r => r.RefreshAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateEventAsync_WithInvalidStatus_ReturnsInvalidStatus()
    {
        _events.Setup(r => r.GetForAdminUpdateAsync(1)).ReturnsAsync(new Event { EventId = 1 });

        var status = await _sut.UpdateEventAsync(1, new UpdateEventRequest("Name", DateTime.UtcNow, "not-a-real-status", null));

        Assert.Equal(AdminEventUpdateStatus.InvalidStatus, status);
    }

    [Fact]
    public async Task GetBookingsAsync_PassesTheSearchTermThroughToTheRepository()
    {
        _bookings.Setup(r => r.AdminSearchAsync("jazz", 1, 25)).ReturnsAsync((0, new List<Booking>()));

        await _sut.GetBookingsAsync("jazz", 1, 25);

        _bookings.Verify(r => r.AdminSearchAsync("jazz", 1, 25), Times.Once);
    }

    [Fact]
    public async Task GetBookingTrendAsync_MapsRepositoryPointsToDtos()
    {
        var today = DateTime.UtcNow.Date;
        _bookings.Setup(r => r.GetDailyTrendAsync(30)).ReturnsAsync(new List<DailyTrendPoint>
        {
            new(today.AddDays(-1), 3, 150m),
            new(today, 5, 275m),
        });

        var trend = await _sut.GetBookingTrendAsync(30);

        Assert.Equal(2, trend.Count);
        Assert.Equal(5, trend[1].Bookings);
        Assert.Equal(275m, trend[1].Revenue);
    }

    [Fact]
    public async Task GetBookingTrendAsync_ClampsDaysToTheAllowedRange()
    {
        _bookings.Setup(r => r.GetDailyTrendAsync(It.IsAny<int>())).ReturnsAsync(new List<DailyTrendPoint>());

        await _sut.GetBookingTrendAsync(500);

        _bookings.Verify(r => r.GetDailyTrendAsync(90), Times.Once);
    }

    [Fact]
    public async Task CreateGateAsync_WhenNameIsUnique_CreatesTheGateAndReturnsIt()
    {
        _gates.Setup(g => g.NameExistsAsync("Gate A", null)).ReturnsAsync(false);

        var (status, gate) = await _sut.CreateGateAsync("Gate A", "Main entrance", adminUserId: 1);

        Assert.Equal(GateCreationStatus.Success, status);
        Assert.NotNull(gate);
        Assert.Equal("Gate A", gate!.Name);
        Assert.Equal("Active", gate.Status);
        _gates.Verify(g => g.AddAsync(It.Is<Gate>(x => x.Name == "Gate A" && x.CreatedByUserId == 1)), Times.Once);
    }

    [Fact]
    public async Task CreateGateAsync_WhenNameAlreadyExists_ReturnsDuplicateNameWithoutCreating()
    {
        _gates.Setup(g => g.NameExistsAsync("Gate A", null)).ReturnsAsync(true);

        var (status, gate) = await _sut.CreateGateAsync("Gate A", null, adminUserId: 1);

        Assert.Equal(GateCreationStatus.DuplicateName, status);
        Assert.Null(gate);
        _gates.Verify(g => g.AddAsync(It.IsAny<Gate>()), Times.Never);
    }

    [Fact]
    public async Task DeleteGateAsync_WhenRepositoryReportsHasHistory_PassesThatStatusThrough()
    {
        _gates.Setup(g => g.DeleteAsync(5)).ReturnsAsync(GateDeleteStatus.HasHistory);

        var status = await _sut.DeleteGateAsync(5);

        Assert.Equal(GateDeleteStatus.HasHistory, status);
    }

    [Fact]
    public async Task CreateGateUserAsync_WhenEmailAlreadyExists_ReturnsEmailAlreadyExistsWithoutCreatingAUser()
    {
        _users.Setup(u => u.EmailExistsAsync("taken@x.com")).ReturnsAsync(true);

        var (status, user) = await _sut.CreateGateUserAsync("Gate Staff", "taken@x.com", "password123", []);

        Assert.Equal(GateUserCreationStatus.EmailAlreadyExists, status);
        Assert.Null(user);
        _users.Verify(u => u.AddAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task CreateGateUserAsync_WhenEmailIsNew_CreatesAGateUserAndAssignsTheGivenGates()
    {
        _users.Setup(u => u.EmailExistsAsync("staff@x.com")).ReturnsAsync(false);
        _gates.Setup(g => g.GetByIdAsync(1)).ReturnsAsync(new Gate { GateId = 1, Name = "Gate A" });
        User? added = null;
        _users.Setup(u => u.AddAsync(It.IsAny<User>())).Callback<User>(u => { u.UserId = 99; added = u; }).Returns(Task.CompletedTask);

        var (status, user) = await _sut.CreateGateUserAsync("Gate Staff", "staff@x.com", "password123", [1]);

        Assert.Equal(GateUserCreationStatus.Success, status);
        Assert.NotNull(user);
        Assert.Equal(99, user!.UserId);
        Assert.NotNull(added);
        Assert.Equal(UserRole.GateUser, added!.Role);
        _gates.Verify(g => g.AssignUserAsync(1, 99, null), Times.Once);
    }

    [Fact]
    public async Task AssignGateUserAsync_WhenTargetUserIsNotAGateUser_ReturnsUserNotGateRole()
    {
        _gates.Setup(g => g.GetByIdAsync(1)).ReturnsAsync(new Gate { GateId = 1, Name = "Gate A" });
        _users.Setup(u => u.GetByIdAsync(9)).ReturnsAsync(new User { UserId = 9, Role = UserRole.Customer });

        var status = await _sut.AssignGateUserAsync(1, 9, assignedByUserId: 2);

        Assert.Equal(GateUserAssignStatus.UserNotGateRole, status);
        _gates.Verify(g => g.AssignUserAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task AssignGateUserAsync_WhenGateAndUserAreValid_AssignsSuccessfully()
    {
        _gates.Setup(g => g.GetByIdAsync(1)).ReturnsAsync(new Gate { GateId = 1, Name = "Gate A" });
        _users.Setup(u => u.GetByIdAsync(9)).ReturnsAsync(new User { UserId = 9, Role = UserRole.GateUser });

        var status = await _sut.AssignGateUserAsync(1, 9, assignedByUserId: 2);

        Assert.Equal(GateUserAssignStatus.Success, status);
        _gates.Verify(g => g.AssignUserAsync(1, 9, 2), Times.Once);
    }

    [Fact]
    public async Task RemoveGateUserAsync_WhenNoSuchAssignmentExists_ReturnsNotFound()
    {
        _gates.Setup(g => g.RemoveUserAsync(1, 9)).ReturnsAsync(false);

        var status = await _sut.RemoveGateUserAsync(1, 9);

        Assert.Equal(GateUserRemoveStatus.NotFound, status);
    }

    [Fact]
    public async Task RemoveGateUserAsync_WhenAssignmentIsRemoved_ReturnsSuccess()
    {
        _gates.Setup(g => g.RemoveUserAsync(1, 9)).ReturnsAsync(true);

        var status = await _sut.RemoveGateUserAsync(1, 9);

        Assert.Equal(GateUserRemoveStatus.Success, status);
    }
}
