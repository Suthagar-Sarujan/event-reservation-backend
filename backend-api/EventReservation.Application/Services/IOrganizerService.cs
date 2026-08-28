using EventReservation.Application.DTOs;

namespace EventReservation.Application.Services;

public interface IOrganizerService
{
    Task<List<VenueOptionDto>> GetVenuesAsync();
    Task<List<OrganizerEventSummaryDto>> GetMyEventsAsync(int organizerUserId);
    Task<OrganizerEventDetailDto?> GetMyEventAsync(long id, int organizerUserId);
    Task<OrganizerEventCreationResult> CreateEventAsync(int organizerUserId, CreateEventRequest request);
    Task<OrganizerUpdateStatus> UpdateEventAsync(long id, int organizerUserId, UpdateEventRequest request);
    Task<OrganizerEventDetailDto?> AddListingAsync(long eventId, int organizerUserId, CreateListingRequest request);
    Task<OrganizerListingUpdateResult> UpdateListingAsync(string listingId, int organizerUserId, UpdateListingRequest request);
    Task<List<OrganizerBookingDto>?> GetEventBookingsAsync(long eventId, int organizerUserId);
    Task<List<TrendPointDto>> GetSalesTrendAsync(int organizerUserId, int days);
    Task<FraudOverviewDto> GetFraudOverviewAsync(int organizerUserId);
}
