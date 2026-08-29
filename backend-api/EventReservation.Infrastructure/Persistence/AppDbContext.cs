using EventReservation.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EventReservation.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Performer> Performers => Set<Performer>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventPerformer> EventPerformers => Set<EventPerformer>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingItem> BookingItems => Set<BookingItem>();
    public DbSet<UserEventTicketCount> UserEventTicketCounts => Set<UserEventTicketCount>();
    public DbSet<BookingRiskAssessment> BookingRiskAssessments => Set<BookingRiskAssessment>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<Gate> Gates => Set<Gate>();
    public DbSet<GateUserAssignment> GateUserAssignments => Set<GateUserAssignment>();
    public DbSet<GateScanHistory> GateScanHistories => Set<GateScanHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // This context maps onto tables already created by scripts/schema.sql -
        // it never runs migrations against the SeatGeek-derived dimension tables,
        // only reads/writes them alongside the app-owned users/bookings tables.

        modelBuilder.Entity<Venue>(e =>
        {
            e.ToTable("venues");
            e.HasKey(v => v.VenueId);
            e.Property(v => v.VenueId).HasColumnName("venue_id").ValueGeneratedOnAdd();
            e.Property(v => v.Name).HasColumnName("name");
            e.Property(v => v.Slug).HasColumnName("slug");
            e.Property(v => v.AddressStreet).HasColumnName("address_street");
            e.Property(v => v.AddressCity).HasColumnName("address_city");
            e.Property(v => v.AddressState).HasColumnName("address_state");
            e.Property(v => v.AddressCountry).HasColumnName("address_country");
            e.Property(v => v.AddressPostalCode).HasColumnName("address_postal_code");
            e.Property(v => v.Timezone).HasColumnName("timezone");
            e.Property(v => v.Latitude).HasColumnName("latitude");
            e.Property(v => v.Longitude).HasColumnName("longitude");
            e.Property(v => v.Capacity).HasColumnName("capacity");
            e.Property(v => v.PopularityScore).HasColumnName("popularity_score");
            e.Property(v => v.PopularityCount).HasColumnName("popularity_count");
            e.Property(v => v.MetroCode).HasColumnName("metro_code");
            e.Property(v => v.CreatedByUserId).HasColumnName("created_by_user_id");
        });

        modelBuilder.Entity<Performer>(e =>
        {
            e.ToTable("performers");
            e.HasKey(p => p.PerformerId);
            e.Property(p => p.PerformerId).HasColumnName("performer_id").ValueGeneratedOnAdd();
            e.Property(p => p.Name).HasColumnName("name");
            e.Property(p => p.ShortName).HasColumnName("short_name");
            e.Property(p => p.Type).HasColumnName("type");
            e.Property(p => p.Slug).HasColumnName("slug");
            e.Property(p => p.TaxonomyName).HasColumnName("taxonomy_name");
            e.Property(p => p.TaxonomySubName).HasColumnName("taxonomy_sub_name");
            e.Property(p => p.HomeVenueId).HasColumnName("home_venue_id");
            e.Property(p => p.Score).HasColumnName("score");
            e.Property(p => p.Popularity).HasColumnName("popularity");
            e.Property(p => p.IsEvent).HasColumnName("is_event");
            e.Property(p => p.DivisionName).HasColumnName("division_name");
            e.Property(p => p.DivisionShortName).HasColumnName("division_short_name");
            e.Property(p => p.CreatedByUserId).HasColumnName("created_by_user_id");
            e.HasOne(p => p.HomeVenue).WithMany(v => v.Performers).HasForeignKey(p => p.HomeVenueId);
        });

        modelBuilder.Entity<Event>(e =>
        {
            e.ToTable("events");
            e.HasKey(ev => ev.EventId);
            e.Property(ev => ev.EventId).HasColumnName("event_id").ValueGeneratedOnAdd();
            e.Property(ev => ev.Name).HasColumnName("name");
            e.Property(ev => ev.ShortName).HasColumnName("short_name");
            e.Property(ev => ev.Type).HasColumnName("type");
            e.Property(ev => ev.TaxonomyName).HasColumnName("taxonomy_name");
            e.Property(ev => ev.TaxonomySubName).HasColumnName("taxonomy_sub_name");
            e.Property(ev => ev.VenueId).HasColumnName("venue_id");
            e.Property(ev => ev.DatetimeUtc).HasColumnName("datetime_utc");
            e.Property(ev => ev.EndDatetimeUtc).HasColumnName("end_datetime_utc");
            e.Property(ev => ev.DateTbd).HasColumnName("date_tbd");
            e.Property(ev => ev.TimeTbd).HasColumnName("time_tbd");
            e.Property(ev => ev.Status).HasColumnName("status");
            e.Property(ev => ev.ScheduleStatus).HasColumnName("schedule_status");
            e.Property(ev => ev.IsOpen).HasColumnName("is_open");
            e.Property(ev => ev.IsGa).HasColumnName("is_ga");
            e.Property(ev => ev.SeatSelectionEnabled).HasColumnName("seat_selection_enabled");
            e.Property(ev => ev.Url).HasColumnName("url");
            e.Property(ev => ev.CreatedAtSource).HasColumnName("created_at_source");
            e.Property(ev => ev.AnnounceDate).HasColumnName("announce_date");
            e.Property(ev => ev.CreatedByUserId).HasColumnName("created_by_user_id");
            e.Property(ev => ev.ImageUrl).HasColumnName("image_url");
            e.HasOne(ev => ev.Venue).WithMany(v => v.Events).HasForeignKey(ev => ev.VenueId);
        });

        modelBuilder.Entity<EventPerformer>(e =>
        {
            e.ToTable("event_performers");
            e.HasKey(ep => new { ep.EventId, ep.PerformerId });
            e.Property(ep => ep.EventId).HasColumnName("event_id");
            e.Property(ep => ep.PerformerId).HasColumnName("performer_id");
            e.HasOne(ep => ep.Event).WithMany(ev => ev.EventPerformers).HasForeignKey(ep => ep.EventId);
            e.HasOne(ep => ep.Performer).WithMany(p => p.EventPerformers).HasForeignKey(ep => ep.PerformerId);
        });

        modelBuilder.Entity<Listing>(e =>
        {
            e.ToTable("listings");
            e.HasKey(l => l.ListingId);
            e.Property(l => l.ListingId).HasColumnName("listing_id").HasColumnType("varchar(100)").ValueGeneratedNever();
            e.Property(l => l.EventId).HasColumnName("event_id");
            e.Property(l => l.Section).HasColumnName("section");
            e.Property(l => l.SectionFull).HasColumnName("section_full");
            e.Property(l => l.RowLabel).HasColumnName("row_label");
            e.Property(l => l.Quantity).HasColumnName("quantity");
            e.Property(l => l.QuantityRemaining).HasColumnName("quantity_remaining");
            e.Property(l => l.DealBucket).HasColumnName("deal_bucket");
            e.Property(l => l.DeliveryType).HasColumnName("delivery_type");
            e.Property(l => l.Marketplace).HasColumnName("marketplace");
            e.Property(l => l.SplitType).HasColumnName("split_type");
            e.Property(l => l.InHandDate).HasColumnName("in_hand_date");
            e.Property(l => l.UnitPrice).HasColumnName("unit_price");
            e.Property(l => l.ListingStatus).HasColumnName("listing_status")
                .HasConversion(v => v == ListingStatus.Available ? "available" : "sold_out",
                               v => v == "available" ? ListingStatus.Available : ListingStatus.SoldOut);
            e.HasOne(l => l.Event).WithMany(ev => ev.Listings).HasForeignKey(l => l.EventId);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(u => u.UserId);
            e.Property(u => u.UserId).HasColumnName("user_id");
            e.Property(u => u.FullName).HasColumnName("full_name");
            e.Property(u => u.Email).HasColumnName("email");
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.PasswordHash).HasColumnName("password_hash");
            e.Property(u => u.Role).HasColumnName("role")
                .HasConversion(
                    v => v == UserRole.Admin ? "admin" : v == UserRole.Organizer ? "organizer" : v == UserRole.GateUser ? "gateuser" : "customer",
                    v => v == "admin" ? UserRole.Admin : v == "organizer" ? UserRole.Organizer : v == "gateuser" ? UserRole.GateUser : UserRole.Customer);
            e.Property(u => u.ThemePreference).HasColumnName("theme_preference")
                .HasConversion(
                    v => v == ThemePreference.Light ? "light" : v == ThemePreference.Dark ? "dark" : "system",
                    v => v == "light" ? ThemePreference.Light : v == "dark" ? ThemePreference.Dark : ThemePreference.System);
            e.Property(u => u.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<Booking>(e =>
        {
            e.ToTable("bookings");
            e.HasKey(b => b.BookingId);
            e.Property(b => b.BookingId).HasColumnName("booking_id");
            e.Property(b => b.BookingReference).HasColumnName("booking_reference");
            e.HasIndex(b => b.BookingReference).IsUnique();
            e.Property(b => b.UserId).HasColumnName("user_id");
            e.Property(b => b.EventId).HasColumnName("event_id");
            e.Property(b => b.Status).HasColumnName("status")
                .HasConversion(v => v == BookingStatus.Confirmed ? "confirmed" : "cancelled",
                               v => v == "confirmed" ? BookingStatus.Confirmed : BookingStatus.Cancelled);
            e.Property(b => b.TotalAmount).HasColumnName("total_amount");
            e.Property(b => b.CreatedAt).HasColumnName("created_at");
            e.Property(b => b.PaymentReference).HasColumnName("payment_reference").HasMaxLength(20);
            e.Property(b => b.CheckedInAt).HasColumnName("checked_in_at");
            e.Property(b => b.CheckedOutAt).HasColumnName("checked_out_at");
            e.HasOne(b => b.User).WithMany(u => u.Bookings).HasForeignKey(b => b.UserId);
            e.HasOne(b => b.Event).WithMany().HasForeignKey(b => b.EventId);
        });

        modelBuilder.Entity<BookingItem>(e =>
        {
            e.ToTable("booking_items");
            e.HasKey(bi => bi.BookingItemId);
            e.Property(bi => bi.BookingItemId).HasColumnName("booking_item_id");
            e.Property(bi => bi.BookingId).HasColumnName("booking_id");
            e.Property(bi => bi.ListingId).HasColumnName("listing_id").HasColumnType("varchar(100)");
            e.Property(bi => bi.Quantity).HasColumnName("quantity");
            e.Property(bi => bi.UnitPrice).HasColumnName("unit_price");
            e.Property(bi => bi.Subtotal).HasColumnName("subtotal");
            e.HasOne(bi => bi.Booking).WithMany(b => b.Items).HasForeignKey(bi => bi.BookingId);
            e.HasOne(bi => bi.Listing).WithMany().HasForeignKey(bi => bi.ListingId);
        });

        modelBuilder.Entity<UserEventTicketCount>(e =>
        {
            e.ToTable("user_event_ticket_counts");
            e.HasKey(x => new { x.UserId, x.EventId });
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.EventId).HasColumnName("event_id");
            e.Property(x => x.TicketsBooked).HasColumnName("tickets_booked");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)").ValueGeneratedOnAddOrUpdate();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BookingRiskAssessment>(e =>
        {
            e.ToTable("booking_risk_assessments");
            e.HasKey(x => x.BookingRiskId);
            e.Property(x => x.BookingRiskId).HasColumnName("booking_risk_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.EventId).HasColumnName("event_id");
            e.Property(x => x.BookingId).HasColumnName("booking_id");
            e.Property(x => x.IpAddress).HasColumnName("ip_address").HasMaxLength(45);
            e.Property(x => x.RequestedQuantity).HasColumnName("requested_quantity");
            e.Property(x => x.RiskScore).HasColumnName("risk_score");
            e.Property(x => x.RiskLevel).HasColumnName("risk_level")
                .HasConversion(
                    v => v == RiskLevel.Low ? "low" : v == RiskLevel.Medium ? "medium" : "high",
                    v => v == "low" ? RiskLevel.Low : v == "medium" ? RiskLevel.Medium : RiskLevel.High);
            e.Property(x => x.Decision).HasColumnName("decision")
                .HasConversion(
                    v => v == RiskDecision.Allowed ? "allowed" : v == RiskDecision.Flagged ? "flagged" : "blocked",
                    v => v == "allowed" ? RiskDecision.Allowed : v == "flagged" ? RiskDecision.Flagged : RiskDecision.Blocked);
            e.Property(x => x.Reasons).HasColumnName("reasons").HasMaxLength(500);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.IpAddress);
            e.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<UserPreference>(e =>
        {
            e.ToTable("user_preferences");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
            e.Property(x => x.EventTypes).HasColumnName("event_types").HasMaxLength(255).IsRequired();
            e.Property(x => x.MusicGenres).HasColumnName("music_genres").HasMaxLength(255).IsRequired();
            e.Property(x => x.Atmosphere).HasColumnName("atmosphere").HasMaxLength(50);
            e.Property(x => x.AttendanceFrequency).HasColumnName("attendance_frequency").HasMaxLength(50);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne(x => x.User).WithOne().HasForeignKey<UserPreference>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Gate>(e =>
        {
            e.ToTable("gates");
            e.HasKey(x => x.GateId);
            e.Property(x => x.GateId).HasColumnName("gate_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(150);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
            e.Property(x => x.Status).HasColumnName("status")
                .HasConversion(
                    v => v == GateStatus.Active ? "active" : "inactive",
                    v => v == "active" ? GateStatus.Active : GateStatus.Inactive);
            e.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<GateUserAssignment>(e =>
        {
            e.ToTable("gate_user_assignments");
            e.HasKey(x => new { x.GateId, x.UserId });
            e.Property(x => x.GateId).HasColumnName("gate_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.AssignedAt).HasColumnName("assigned_at");
            e.Property(x => x.AssignedByUserId).HasColumnName("assigned_by_user_id");
            e.HasOne(x => x.Gate).WithMany(g => g.Assignments).HasForeignKey(x => x.GateId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<GateScanHistory>(e =>
        {
            e.ToTable("gate_scan_histories");
            e.HasKey(x => x.ScanId);
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.GateId).HasColumnName("gate_id");
            e.Property(x => x.ScannedByUserId).HasColumnName("scanned_by_user_id");
            e.Property(x => x.BookingId).HasColumnName("booking_id");
            e.Property(x => x.ScannedCode).HasColumnName("scanned_code").HasMaxLength(255);
            e.Property(x => x.EventId).HasColumnName("event_id");
            e.Property(x => x.ScanType).HasColumnName("scan_type")
                .HasConversion(
                    v => v == GateScanType.CheckOut ? "checkout" : "checkin",
                    v => v == "checkout" ? GateScanType.CheckOut : GateScanType.CheckIn);
            e.Property(x => x.Status).HasColumnName("status")
                .HasConversion(
                    v => v == GateScanStatus.Success ? "success" : "failed",
                    v => v == "success" ? GateScanStatus.Success : GateScanStatus.Failed);
            e.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
            e.Property(x => x.ScannedAt).HasColumnName("scanned_at");
            e.HasOne(x => x.Gate).WithMany().HasForeignKey(x => x.GateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ScannedByUser).WithMany().HasForeignKey(x => x.ScannedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.GateId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ScannedAt);
        });
    }
}
