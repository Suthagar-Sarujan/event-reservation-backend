namespace EventReservation.Application.Services;

public enum AdminRoleUpdateStatus
{
    Success,
    InvalidRole,
    CantRemoveOwnAdmin,
    UserNotFound,
}

public enum AdminEventUpdateStatus
{
    Success,
    NotFound,
    InvalidStatus,
}

public enum GateCreationStatus
{
    Success,
    DuplicateName,
}

public enum GateUpdateStatus
{
    Success,
    NotFound,
    DuplicateName,
}

public enum GateStatusChangeStatus
{
    Success,
    NotFound,
}

public enum GateUserCreationStatus
{
    Success,
    EmailAlreadyExists,
    GateNotFound,
}

public enum GateUserAssignStatus
{
    Success,
    GateNotFound,
    UserNotFound,
    UserNotGateRole,
}

public enum GateUserRemoveStatus
{
    Success,
    NotFound,
}
