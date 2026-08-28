using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventReservation.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFraudDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "booking_risk_assessments",
                columns: table => new
                {
                    booking_risk_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<int>(type: "int", nullable: false),
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    booking_id = table.Column<int>(type: "int", nullable: true),
                    ip_address = table.Column<string>(type: "varchar(45)", maxLength: 45, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    requested_quantity = table.Column<int>(type: "int", nullable: false),
                    risk_score = table.Column<int>(type: "int", nullable: false),
                    risk_level = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    decision = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reasons = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_booking_risk_assessments", x => x.booking_risk_id);
                    table.ForeignKey(
                        name: "FK_booking_risk_assessments_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "booking_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_booking_risk_assessments_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_booking_risk_assessments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user_event_ticket_counts",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "int", nullable: false),
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    tickets_booked = table.Column<int>(type: "int", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_event_ticket_counts", x => new { x.user_id, x.event_id });
                    table.ForeignKey(
                        name: "FK_user_event_ticket_counts_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_event_ticket_counts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_booking_risk_assessments_booking_id",
                table: "booking_risk_assessments",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "IX_booking_risk_assessments_created_at",
                table: "booking_risk_assessments",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_booking_risk_assessments_event_id",
                table: "booking_risk_assessments",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "IX_booking_risk_assessments_ip_address",
                table: "booking_risk_assessments",
                column: "ip_address");

            migrationBuilder.CreateIndex(
                name: "IX_booking_risk_assessments_user_id",
                table: "booking_risk_assessments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_event_ticket_counts_event_id",
                table: "user_event_ticket_counts",
                column: "event_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_risk_assessments");

            migrationBuilder.DropTable(
                name: "user_event_ticket_counts");
        }
    }
}
