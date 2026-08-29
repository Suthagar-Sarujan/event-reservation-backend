using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventReservation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGateManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gates",
                columns: table => new
                {
                    gate_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_by_user_id = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gates", x => x.gate_id);
                    table.ForeignKey(
                        name: "FK_gates_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "gate_scan_histories",
                columns: table => new
                {
                    scan_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    gate_id = table.Column<int>(type: "int", nullable: false),
                    scanned_by_user_id = table.Column<int>(type: "int", nullable: false),
                    booking_id = table.Column<int>(type: "int", nullable: true),
                    scanned_code = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    event_id = table.Column<long>(type: "bigint", nullable: true),
                    scan_type = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    failure_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    scanned_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gate_scan_histories", x => x.scan_id);
                    table.ForeignKey(
                        name: "FK_gate_scan_histories_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "booking_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_gate_scan_histories_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_gate_scan_histories_gates_gate_id",
                        column: x => x.gate_id,
                        principalTable: "gates",
                        principalColumn: "gate_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gate_scan_histories_users_scanned_by_user_id",
                        column: x => x.scanned_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "gate_user_assignments",
                columns: table => new
                {
                    gate_id = table.Column<int>(type: "int", nullable: false),
                    user_id = table.Column<int>(type: "int", nullable: false),
                    assigned_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    assigned_by_user_id = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gate_user_assignments", x => new { x.gate_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_gate_user_assignments_gates_gate_id",
                        column: x => x.gate_id,
                        principalTable: "gates",
                        principalColumn: "gate_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_gate_user_assignments_users_assigned_by_user_id",
                        column: x => x.assigned_by_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_gate_user_assignments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_gate_scan_histories_booking_id",
                table: "gate_scan_histories",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "IX_gate_scan_histories_event_id",
                table: "gate_scan_histories",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "IX_gate_scan_histories_gate_id",
                table: "gate_scan_histories",
                column: "gate_id");

            migrationBuilder.CreateIndex(
                name: "IX_gate_scan_histories_scanned_at",
                table: "gate_scan_histories",
                column: "scanned_at");

            migrationBuilder.CreateIndex(
                name: "IX_gate_scan_histories_scanned_by_user_id",
                table: "gate_scan_histories",
                column: "scanned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_gate_scan_histories_status",
                table: "gate_scan_histories",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_gate_user_assignments_assigned_by_user_id",
                table: "gate_user_assignments",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_gate_user_assignments_user_id",
                table: "gate_user_assignments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_gates_created_by_user_id",
                table: "gates",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_gates_name",
                table: "gates",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gates_status",
                table: "gates",
                column: "status");

            // users.role is a native MySQL ENUM (see scripts/schema.sql) rather
            // than a plain varchar, but EF's model only sees it as a
            // string-converted property (User.Role's HasConversion), so
            // `dotnet ef migrations add` can't detect that the underlying
            // ENUM's allowed-value list needs to grow - it has to be widened
            // by hand here or every gate-user account creation fails with
            // MySQL's "Data truncated for column 'role'".
            migrationBuilder.Sql("ALTER TABLE `users` MODIFY COLUMN `role` ENUM('customer','organizer','admin','gateuser') NOT NULL DEFAULT 'customer';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Narrowing back to 3 values will fail if any row still has
            // role='gateuser' - reassign/delete those users before rolling
            // this migration back.
            migrationBuilder.Sql("ALTER TABLE `users` MODIFY COLUMN `role` ENUM('customer','organizer','admin') NOT NULL DEFAULT 'customer';");

            migrationBuilder.DropTable(
                name: "gate_scan_histories");

            migrationBuilder.DropTable(
                name: "gate_user_assignments");

            migrationBuilder.DropTable(
                name: "gates");
        }
    }
}
