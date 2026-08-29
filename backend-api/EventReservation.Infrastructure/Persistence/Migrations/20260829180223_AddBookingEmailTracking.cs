using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventReservation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingEmailTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "email_attempts",
                table: "bookings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "email_sent_at",
                table: "bookings",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email_status",
                table: "bookings",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "pending")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "email_attempts",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "email_sent_at",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "email_status",
                table: "bookings");
        }
    }
}
