using System.Reflection;
using EventReservation.Application.Repositories;
using EventReservation.Application.Services;
using EventReservation.Domain.Entities;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Utils;

namespace EventReservation.Infrastructure.Services;

public class SmtpEmailService : IEmailService
{
    private readonly IBookingRepository _bookings;
    private readonly IUserRepository _users;
    private readonly IQrCodeService _qr;
    private readonly SmtpOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(
        IBookingRepository bookings,
        IUserRepository users,
        IQrCodeService qr,
        IOptions<SmtpOptions> options,
        IConfiguration configuration,
        ILogger<SmtpEmailService> logger)
    {
        _bookings = bookings;
        _users = users;
        _qr = qr;
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendBookingConfirmationAsync(int bookingId)
    {
        var booking = await _bookings.GetForVerificationAsync(bookingId);
        if (booking is null || booking.Event is null || booking.User is null)
        {
            return EmailSendResult.BookingNotFound;
        }

        try
        {
            var token = _qr.GenerateToken(booking.BookingId, booking.BookingReference);
            var qrBytes = _qr.GeneratePngBytes(token);

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.FromDisplayName, _options.FromAddress));
            message.To.Add(new MailboxAddress(booking.User.FullName, booking.User.Email));
            message.Subject = $"\U0001F39F️ Eventify – Your Booking is Confirmed | Booking ID: {booking.BookingReference}";

            var builder = new BodyBuilder();
            var qrImage = builder.LinkedResources.Add("qr-code.png", qrBytes, new ContentType("image", "png"));
            qrImage.ContentId = MimeUtils.GenerateMessageId();

            var logoImage = builder.LinkedResources.Add("eventify-logo.png", LoadLogoBytes(), new ContentType("image", "png"));
            logoImage.ContentId = MimeUtils.GenerateMessageId();

            var frontendBaseUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:4200";
            var ticketUrl = $"{frontendBaseUrl.TrimEnd('/')}/bookings/{booking.BookingId}/ticket";
            var quantity = booking.Items.Sum(i => i.Quantity);

            builder.HtmlBody = BuildHtmlBody(booking, qrImage.ContentId, logoImage.ContentId, ticketUrl, quantity);
            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_options.User, _options.Password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            await _bookings.MarkEmailResultAsync(bookingId, BookingEmailStatus.Sent, booking.EmailAttempts + 1, DateTime.UtcNow);
            return EmailSendResult.Sent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send booking confirmation email for booking {BookingId}", bookingId);
            await _bookings.MarkEmailResultAsync(bookingId, BookingEmailStatus.Failed, booking.EmailAttempts + 1, null);
            return EmailSendResult.Failed;
        }
    }

    public async Task<bool> SendPasswordResetAsync(int userId, string rawToken)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user is null)
        {
            return false;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.FromDisplayName, _options.FromAddress));
            message.To.Add(new MailboxAddress(user.FullName, user.Email));
            message.Subject = "Eventify – Reset Your Password";

            var builder = new BodyBuilder();
            var logoImage = builder.LinkedResources.Add("eventify-logo.png", LoadLogoBytes(), new ContentType("image", "png"));
            logoImage.ContentId = MimeUtils.GenerateMessageId();

            var frontendBaseUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:4200";
            // rawToken is already URL-safe (base64url, no padding) - no
            // further encoding needed for the query string.
            var resetUrl = $"{frontendBaseUrl.TrimEnd('/')}/reset-password?token={rawToken}";

            builder.HtmlBody = BuildPasswordResetHtmlBody(user.FullName, logoImage.ContentId, resetUrl);
            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_options.User, _options.Password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            return true;
        }
        catch (Exception ex)
        {
            // Never log rawToken or any password - only enough to find the
            // failure in the logs, per the feature's explicit security rules.
            _logger.LogError(ex, "Failed to send password reset email for user {UserId}", userId);
            return false;
        }
    }

    private static string BuildPasswordResetHtmlBody(string fullName, string logoContentId, string resetUrl)
    {
        return $$"""
            <div style="font-family: Arial, Helvetica, sans-serif; background-color: #f4f4f7; padding: 24px;">
              <div style="max-width: 560px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; overflow: hidden; border: 1px solid #e5e5e5;">
                <div style="{{BrandGradientStyle}} padding: 24px; text-align: center;">
                  <img src="cid:{{logoContentId}}" alt="Eventify" style="height: 36px;" />
                </div>
                <div style="padding: 24px;">
                  <p style="font-size: 16px; color: #111827;">Hi {{fullName}},</p>
                  <p style="font-size: 15px; color: #374151;">We received a request to reset your Eventify account password.</p>
                  <p style="font-size: 15px; color: #374151;">Click the button below to create a new password:</p>
                  <div style="text-align: center; margin: 24px 0;">
                    <a href="{{resetUrl}}" style="{{BrandGradientStyle}} color: #ffffff; text-decoration: none; padding: 12px 24px; border-radius: 6px; font-size: 14px; font-weight: bold; display: inline-block;">Reset Password</a>
                  </div>
                  <p style="font-size: 13px; color: #6b7280;">This password reset link will expire in 15 minutes and can only be used once.</p>
                  <p style="font-size: 13px; color: #6b7280;">If you did not request a password reset, you can safely ignore this email.</p>
                  <p style="font-size: 13px; color: #9ca3af; text-align: center; margin-top: 32px;">Eventify – Smart Event Ticketing</p>
                </div>
              </div>
            </div>
            """;
    }

    // Compiled into the assembly (see the EmbeddedResource item in the .csproj)
    // rather than read from a file path, so this can never go missing at
    // runtime regardless of working directory or how the app is published.
    private static byte[] LoadLogoBytes()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().First(n => n.EndsWith("logo-white.png", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    // Matches the frontend's --gradient-brand token (styles.css:
    // linear-gradient(120deg, #0b2062 0%, #071a47 45%, #2b4c92 100%)) - email
    // HTML can't reference CSS custom properties, so the same navy values are
    // hardcoded here to keep the two in visual sync. A solid background-color
    // fallback is included first for clients (older Outlook) that don't
    // render CSS gradients.
    private const string BrandGradientStyle = "background-color: #0b2062; background-image: linear-gradient(120deg, #0b2062 0%, #071a47 45%, #2b4c92 100%);";

    private static string BuildHtmlBody(Booking booking, string qrContentId, string logoContentId, string ticketUrl, int quantity)
    {
        var venueName = booking.Event!.Venue?.Name ?? "TBA";
        return $$"""
            <div style="font-family: Arial, Helvetica, sans-serif; background-color: #f4f4f7; padding: 24px;">
              <div style="max-width: 560px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; overflow: hidden; border: 1px solid #e5e5e5;">
                <div style="{{BrandGradientStyle}} padding: 24px; text-align: center;">
                  <img src="cid:{{logoContentId}}" alt="Eventify" style="height: 36px;" />
                </div>
                <div style="padding: 24px;">
                  <p style="font-size: 16px; color: #111827;">Hi {{booking.User!.FullName}},</p>
                  <p style="font-size: 15px; color: #374151;">Your booking has been successfully confirmed! Here are your booking details:</p>
                  <table style="width: 100%; border-collapse: collapse; margin: 16px 0; font-size: 14px; color: #111827;">
                    <tr><td style="padding: 6px 0; color: #6b7280;">Event</td><td style="padding: 6px 0; text-align: right; font-weight: bold;">{{booking.Event.Name}}</td></tr>
                    <tr><td style="padding: 6px 0; color: #6b7280;">Date</td><td style="padding: 6px 0; text-align: right;">{{booking.Event.DatetimeUtc:f}}</td></tr>
                    <tr><td style="padding: 6px 0; color: #6b7280;">Venue</td><td style="padding: 6px 0; text-align: right;">{{venueName}}</td></tr>
                    <tr><td style="padding: 6px 0; color: #6b7280;">Tickets</td><td style="padding: 6px 0; text-align: right;">{{quantity}}</td></tr>
                    <tr><td style="padding: 6px 0; color: #6b7280;">Booking ID</td><td style="padding: 6px 0; text-align: right; font-weight: bold;">{{booking.BookingReference}}</td></tr>
                    <tr><td style="padding: 6px 0; color: #6b7280;">Booking Date</td><td style="padding: 6px 0; text-align: right;">{{booking.CreatedAt:f}}</td></tr>
                    <tr><td style="padding: 6px 0; color: #6b7280;">Status</td><td style="padding: 6px 0; text-align: right; color: #059669; font-weight: bold;">CONFIRMED</td></tr>
                    <tr><td style="padding: 6px 0; color: #6b7280;">Total Amount</td><td style="padding: 6px 0; text-align: right; font-weight: bold;">{{booking.TotalAmount:C}}</td></tr>
                  </table>
                  <div style="text-align: center; margin: 24px 0;">
                    <img src="cid:{{qrContentId}}" alt="Booking QR Code" style="width: 180px; height: 180px; border: 1px solid #e5e5e5; padding: 8px; border-radius: 4px;" />
                    <p style="font-size: 13px; color: #6b7280; margin-top: 8px;">Please present the QR code at the event entrance for ticket verification.</p>
                  </div>
                  <div style="text-align: center; margin: 24px 0;">
                    <a href="{{ticketUrl}}" style="{{BrandGradientStyle}} color: #ffffff; text-decoration: none; padding: 12px 24px; border-radius: 6px; font-size: 14px; font-weight: bold; display: inline-block;">View Digital Ticket</a>
                  </div>
                  <p style="font-size: 13px; color: #9ca3af; text-align: center; margin-top: 32px;">Eventify – Smart Event Ticketing</p>
                </div>
              </div>
            </div>
            """;
    }
}
