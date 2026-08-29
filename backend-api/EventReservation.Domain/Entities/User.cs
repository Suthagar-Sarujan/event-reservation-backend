namespace EventReservation.Domain.Entities;

public enum UserRole
{
    Customer,
    Organizer,
    Admin,
    GateUser,
}

public enum ThemePreference
{
    Light,
    Dark,
    System,
}

public class User
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Customer;
    public ThemePreference ThemePreference { get; set; } = ThemePreference.System;
    public DateTime CreatedAt { get; set; }

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
