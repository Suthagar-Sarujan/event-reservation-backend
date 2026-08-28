namespace EventReservation.Api.Services;

/// <summary>Bound from the "Fraud" section of appsettings.json - see Program.cs.</summary>
public class FraudOptions
{
    public int MaxTicketsPerUserPerEvent { get; set; } = 2;
    public int VelocityWindowMinutes { get; set; } = 10;
    public int VelocityBookingThreshold { get; set; } = 3;
    public int LargeQuantityThreshold { get; set; } = 5;
    public RiskThresholdOptions RiskThresholds { get; set; } = new();
}

public class RiskThresholdOptions
{
    public int MediumMin { get; set; } = 31;
    public int HighMin { get; set; } = 71;
}
