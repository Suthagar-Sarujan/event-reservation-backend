using System.ComponentModel.DataAnnotations;

namespace EventReservation.Application.DTOs;

public record GateScanRequest([Required] int GateId, [Required] string Code, [Required] long EventId, [Required] string ScanType); // "CheckIn" | "CheckOut"

public record GateScanResultDto(bool Success, string Message, string? BookingReference, string? AttendeeName, string? EventName, DateTime? ScannedAt, int? TotalQuantity);

public record GateScanHistoryDto(long ScanId, int GateId, string GateName, int ScannedByUserId, string ScannedByName, int? BookingId, string? BookingReference, string? EventName, string ScanType, string Status, string? FailureReason, DateTime ScannedAt);
