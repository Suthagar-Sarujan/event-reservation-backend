namespace EventReservation.Application.Services;

/// <summary>
/// Bound from the "Smtp" section of appsettings.json - see Program.cs.
/// Password is deliberately left empty in appsettings.json and MUST be
/// supplied via the Smtp__Password environment variable (or, for local dev,
/// `dotnet user-secrets set "Smtp:Password" "<value>"`) - never committed.
/// </summary>
public class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromDisplayName { get; set; } = "Eventify";
}
