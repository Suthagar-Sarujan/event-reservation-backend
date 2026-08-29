using System.Security.Cryptography;
using System.Text;
using EventReservation.Application.Services;
using Microsoft.Extensions.Configuration;
using QRCoder;

namespace EventReservation.Infrastructure.Services;

public class QrCodeService : IQrCodeService
{
    private readonly byte[] _secretBytes;

    public QrCodeService(IConfiguration configuration)
    {
        var secret = configuration["Ticketing:QrSecret"]
            ?? throw new InvalidOperationException("Ticketing:QrSecret is not configured.");
        _secretBytes = Encoding.UTF8.GetBytes(secret);
    }

    public string GenerateToken(int bookingId, string bookingReference)
    {
        var payload = $"{bookingId}:{bookingReference}";
        var signature = Sign(payload);
        return $"{bookingId}.{Base64UrlEncode(signature)}";
    }

    public bool TryValidateToken(string token, string expectedBookingReference, out int bookingId)
    {
        bookingId = 0;
        var parts = token.Split('.', 2);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var parsedId))
        {
            return false;
        }

        var expectedPayload = $"{parsedId}:{expectedBookingReference}";
        var expectedSignature = Sign(expectedPayload);
        byte[] providedSignature;
        try
        {
            providedSignature = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, providedSignature))
        {
            return false;
        }

        bookingId = parsedId;
        return true;
    }

    public bool TryReadBookingId(string token, out int bookingId)
    {
        bookingId = 0;
        var parts = token.Split('.', 2);
        return parts.Length == 2 && int.TryParse(parts[0], out bookingId);
    }

    public string GeneratePngDataUri(string content) =>
        $"data:image/png;base64,{Convert.ToBase64String(GeneratePngBytes(content))}";

    public byte[] GeneratePngBytes(string content)
    {
        using var generator = new QRCodeGenerator();
        using var qrCodeData = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var pngQrCode = new PngByteQRCode(qrCodeData);
        return pngQrCode.GetGraphic(10);
    }

    private byte[] Sign(string payload) => HMACSHA256.HashData(_secretBytes, Encoding.UTF8.GetBytes(payload));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
