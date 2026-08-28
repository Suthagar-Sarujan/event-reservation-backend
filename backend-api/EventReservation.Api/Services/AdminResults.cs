namespace EventReservation.Api.Services;

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
