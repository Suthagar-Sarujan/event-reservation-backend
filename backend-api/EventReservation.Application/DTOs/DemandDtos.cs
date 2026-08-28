namespace EventReservation.Application.DTOs;

public record DemandPredictionDto(
    long EventId,
    string EventName,
    DateTime? DatetimeUtc,
    int Capacity,
    int CurrentBookings,
    int PredictedDemand,
    double ExpectedOccupancy,
    string DemandLevel
);

public record DemandModelInfoDto(
    string? Version,
    DateTime? TrainedAt,
    int TrainingRowCount,
    string Mode,
    double? Mae
);
