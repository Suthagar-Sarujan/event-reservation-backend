using EventReservation.Application.DTOs;
using EventReservation.Domain.Entities;

namespace EventReservation.Application.Services;

public interface IGateService
{
    /// <summary>Gates this Gate User is assigned to AND currently Active.</summary>
    Task<List<GateDto>> GetMyGatesAsync(int gateUserId);

    Task<GateScanResultDto> ScanTicketAsync(int gateUserId, int gateId, string code, long eventId, GateScanType scanType);
}
