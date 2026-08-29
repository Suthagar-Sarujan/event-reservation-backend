using EventReservation.Domain.Entities;

namespace EventReservation.Application.Repositories;

public interface IGateScanRepository
{
    Task LogAsync(GateScanHistory scan);

    /// <summary>
    /// Paged/filtered scan history for the admin audit view, newest first -
    /// mirrors IFraudRepository.GetPlatformWideAsync. Includes Gate/ScannedByUser/Booking/Event for DTO mapping.
    /// </summary>
    Task<(int Total, List<GateScanHistory> Items)> SearchAsync(int? gateId, GateScanStatus? status, DateTime? fromUtc, DateTime? toUtc, int page, int pageSize);
}
