using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace kindlekeep_api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetryTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecurityAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MonitorId = table.Column<Guid>(type: "uuid", nullable: false),
                    SslExpiryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SslIssuer = table.Column<string>(type: "text", nullable: true),
                    HasCsp = table.Column<bool>(type: "boolean", nullable: false),
                    HasHsts = table.Column<bool>(type: "boolean", nullable: false),
                    HasXfo = table.Column<bool>(type: "boolean", nullable: false),
                    HasNosniff = table.Column<bool>(type: "boolean", nullable: false),
                    TlsVersion = table.Column<string>(type: "text", nullable: true),
                    RawHeaders = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityAudits_MonitorTargets_MonitorId",
                        column: x => x.MonitorId,
                        principalTable: "MonitorTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UptimeLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MonitorId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    IsColdStart = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UptimeLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UptimeLogs_MonitorTargets_MonitorId",
                        column: x => x.MonitorId,
                        principalTable: "MonitorTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAudits_MonitorId",
                table: "SecurityAudits",
                column: "MonitorId");

            migrationBuilder.CreateIndex(
                name: "IX_UptimeLogs_MonitorId",
                table: "UptimeLogs",
                column: "MonitorId");

            migrationBuilder.CreateIndex(
                name: "IX_UptimeLogs_Timestamp",
                table: "UptimeLogs",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SecurityAudits");

            migrationBuilder.DropTable(
                name: "UptimeLogs");
        }
    }
}
