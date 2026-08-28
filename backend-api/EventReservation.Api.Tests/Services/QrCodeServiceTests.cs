using EventReservation.Api.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace EventReservation.Api.Tests.Services;

public class QrCodeServiceTests
{
    private readonly QrCodeService _sut;

    public QrCodeServiceTests()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["Ticketing:QrSecret"]).Returns("unit-test-qr-secret");
        _sut = new QrCodeService(config.Object);
    }

    [Fact]
    public void GenerateToken_ThenTryValidateToken_WithTheSameReference_Succeeds()
    {
        var token = _sut.GenerateToken(42, "BKG-ABC123");

        var valid = _sut.TryValidateToken(token, "BKG-ABC123", out var bookingId);

        Assert.True(valid);
        Assert.Equal(42, bookingId);
    }

    [Fact]
    public void TryValidateToken_WhenReferenceDoesNotMatchWhatWasSigned_Fails()
    {
        var token = _sut.GenerateToken(42, "BKG-ABC123");

        var valid = _sut.TryValidateToken(token, "BKG-DIFFERENT", out _);

        Assert.False(valid);
    }

    [Fact]
    public void TryValidateToken_WhenTokenHasBeenTamperedWith_Fails()
    {
        var token = _sut.GenerateToken(42, "BKG-ABC123");
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        var valid = _sut.TryValidateToken(tampered, "BKG-ABC123", out _);

        Assert.False(valid);
    }

    [Fact]
    public void TryReadBookingId_ReadsTheIdWithoutValidatingTheSignature()
    {
        var token = _sut.GenerateToken(99, "BKG-XYZ");

        var ok = _sut.TryReadBookingId(token, out var bookingId);

        Assert.True(ok);
        Assert.Equal(99, bookingId);
    }

    [Fact]
    public void GeneratePngDataUri_ProducesAPngDataUri()
    {
        var uri = _sut.GeneratePngDataUri("some-content");

        Assert.StartsWith("data:image/png;base64,", uri);
        Assert.True(uri.Length > 100);
    }
}
