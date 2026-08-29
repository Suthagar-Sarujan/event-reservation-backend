using EventReservation.Domain.Entities;
using EventReservation.Application.DTOs;
using EventReservation.Application.Repositories;

namespace EventReservation.Application.Services;

public class GateService : IGateService
{
    private const string UnauthorizedGateMessage = "You are not authorized to scan at this gate.";
    private const string InvalidTicketMessage = "Invalid ticket.";
    private const string TicketNotFoundMessage = "Ticket not found.";

    // Copied verbatim from BookingService.VerifyTicketAsync's signature-mismatch
    // wording, for consistency between the two verification flows.
    private const string InvalidSignatureMessage = "Invalid or altered code. This ticket could not be cryptographically verified and was rejected.";
    private const string CancelledMessage = "This ticket has been cancelled.";
    private const string WrongEventMessage = "This ticket is not valid for this event.";
    private const string AlreadyUsedMessage = "This ticket has already been used.";
    private const string NotCheckedInMessage = "This ticket has not been checked in yet.";
    private const string AlreadyCheckedOutMessage = "This ticket has already been checked out.";

    private readonly IGateRepository _gates;
    private readonly IGateScanRepository _gateScans;
    private readonly IBookingRepository _bookings;
    private readonly IQrCodeService _qr;

    public GateService(IGateRepository gates, IGateScanRepository gateScans, IBookingRepository bookings, IQrCodeService qr)
    {
        _gates = gates;
        _gateScans = gateScans;
        _bookings = bookings;
        _qr = qr;
    }

    public async Task<List<GateDto>> GetMyGatesAsync(int gateUserId)
    {
        var gates = await _gates.GetAssignedActiveGatesForUserAsync(gateUserId);
        return gates.Select(g => new GateDto(g.GateId, g.Name, g.Description, g.Status.ToString(), g.Assignments.Count, g.CreatedAt, g.UpdatedAt)).ToList();
    }

    public async Task<GateScanResultDto> ScanTicketAsync(int gateUserId, int gateId, string code, long eventId, GateScanType scanType)
    {
        // 1. Gate permission check - deliberately first, before any ticket detail
        // is looked at, so an unauthorized/wrong-gate attempt never gets to see
        // any ticket information. One message covers "gate doesn't exist",
        // "gate inactive", and "not assigned" - never reveal which.
        var gate = await _gates.GetByIdAsync(gateId);
        if (gate is null || gate.Status != GateStatus.Active || !await _gates.IsUserAssignedToGateAsync(gateUserId, gateId))
        {
            return await FailAsync(gateId, gateUserId, code, null, eventId, scanType, UnauthorizedGateMessage);
        }

        var trimmedCode = code.Trim();
        Booking? booking;

        if (trimmedCode.Contains('.'))
        {
            if (!_qr.TryReadBookingId(trimmedCode, out var bookingId))
            {
                return await FailAsync(gateId, gateUserId, trimmedCode, null, eventId, scanType, InvalidTicketMessage);
            }

            booking = await _bookings.GetForVerificationAsync(bookingId);
            if (booking is null)
            {
                return await FailAsync(gateId, gateUserId, trimmedCode, null, eventId, scanType, TicketNotFoundMessage);
            }

            if (!_qr.TryValidateToken(trimmedCode, booking.BookingReference, out _))
            {
                // A booking with this id exists but the signature doesn't match -
                // never surface attendee details for a code that fails
                // cryptographic verification.
                return await FailAsync(gateId, gateUserId, trimmedCode, booking.BookingId, booking.EventId, scanType, InvalidSignatureMessage);
            }
        }
        else
        {
            booking = await _bookings.GetForVerificationByReferenceAsync(trimmedCode);
            if (booking is null)
            {
                return await FailAsync(gateId, gateUserId, trimmedCode, null, eventId, scanType, TicketNotFoundMessage);
            }
        }

        if (booking.Status == BookingStatus.Cancelled)
        {
            return await FailAsync(gateId, gateUserId, trimmedCode, booking.BookingId, booking.EventId, scanType, CancelledMessage, booking);
        }

        if (booking.EventId != eventId)
        {
            return await FailAsync(gateId, gateUserId, trimmedCode, booking.BookingId, booking.EventId, scanType, WrongEventMessage, booking);
        }

        return scanType == GateScanType.CheckIn
            ? await CheckInAsync(gateId, gateUserId, trimmedCode, booking, scanType)
            : await CheckOutAsync(gateId, gateUserId, trimmedCode, booking, scanType);
    }

    private async Task<GateScanResultDto> CheckInAsync(int gateId, int gateUserId, string trimmedCode, Booking booking, GateScanType scanType)
    {
        if (booking.CheckedInAt is not null)
        {
            return await FailAsync(gateId, gateUserId, trimmedCode, booking.BookingId, booking.EventId, scanType, AlreadyUsedMessage, booking);
        }

        var markedIn = await _bookings.TryMarkCheckedInAsync(booking.BookingId);
        if (!markedIn)
        {
            // Lost a race to a concurrent scan of the same ticket.
            return await FailAsync(gateId, gateUserId, trimmedCode, booking.BookingId, booking.EventId, scanType, AlreadyUsedMessage, booking);
        }

        return await SucceedAsync(gateId, gateUserId, trimmedCode, booking, scanType, "Valid ticket. Checked in successfully.");
    }

    private async Task<GateScanResultDto> CheckOutAsync(int gateId, int gateUserId, string trimmedCode, Booking booking, GateScanType scanType)
    {
        if (booking.CheckedInAt is null)
        {
            return await FailAsync(gateId, gateUserId, trimmedCode, booking.BookingId, booking.EventId, scanType, NotCheckedInMessage, booking);
        }

        if (booking.CheckedOutAt is not null)
        {
            return await FailAsync(gateId, gateUserId, trimmedCode, booking.BookingId, booking.EventId, scanType, AlreadyCheckedOutMessage, booking);
        }

        var markedOut = await _bookings.TryMarkCheckedOutAsync(booking.BookingId);
        if (!markedOut)
        {
            // Lost a race to a concurrent scan of the same ticket.
            return await FailAsync(gateId, gateUserId, trimmedCode, booking.BookingId, booking.EventId, scanType, AlreadyCheckedOutMessage, booking);
        }

        return await SucceedAsync(gateId, gateUserId, trimmedCode, booking, scanType, "Checked out successfully.");
    }

    private async Task<GateScanResultDto> SucceedAsync(int gateId, int gateUserId, string trimmedCode, Booking booking, GateScanType scanType, string message)
    {
        var scannedAt = DateTime.UtcNow;
        await _gateScans.LogAsync(new GateScanHistory
        {
            GateId = gateId,
            ScannedByUserId = gateUserId,
            BookingId = booking.BookingId,
            ScannedCode = trimmedCode,
            EventId = booking.EventId,
            ScanType = scanType,
            Status = GateScanStatus.Success,
            FailureReason = null,
            ScannedAt = scannedAt,
        });

        return new GateScanResultDto(
            true,
            message,
            booking.BookingReference,
            booking.User?.FullName,
            booking.Event?.Name,
            scannedAt,
            booking.Items.Sum(i => i.Quantity));
    }

    /// <summary>
    /// Logs a failed (or, for the gate-permission case, unauthorized) scan
    /// attempt and returns the resulting DTO. Every rejection path funnels
    /// through here so logging can never be accidentally skipped on one branch.
    /// </summary>
    private async Task<GateScanResultDto> FailAsync(int gateId, int gateUserId, string scannedCode, int? bookingId, long? eventId, GateScanType scanType, string message, Booking? booking = null)
    {
        var scannedAt = DateTime.UtcNow;
        await _gateScans.LogAsync(new GateScanHistory
        {
            GateId = gateId,
            ScannedByUserId = gateUserId,
            BookingId = bookingId,
            ScannedCode = scannedCode,
            EventId = eventId,
            ScanType = scanType,
            Status = GateScanStatus.Failed,
            FailureReason = message,
            ScannedAt = scannedAt,
        });

        // Never leak attendee/booking details for the gate-permission failure
        // (no booking was even looked up) or the signature-mismatch failure
        // (a code that failed cryptographic verification must not be trusted
        // enough to surface who it claims to belong to).
        if (booking is null || message == InvalidSignatureMessage)
        {
            return new GateScanResultDto(false, message, null, null, null, null, null);
        }

        return new GateScanResultDto(
            false,
            message,
            booking.BookingReference,
            booking.User?.FullName,
            booking.Event?.Name,
            scannedAt,
            booking.Items.Sum(i => i.Quantity));
    }
}
