using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventReservation.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentAndCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "checked_in_at",
                table: "bookings",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_reference",
                table: "bookings",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "checked_in_at",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "payment_reference",
                table: "bookings");
        }
    }
}
