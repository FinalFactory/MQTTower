using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MQTTower.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultiBroker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BrokerId",
                table: "TopicWatchers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BrokerId",
                table: "ScheduledTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BrokerId",
                table: "MetricSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BrokerId",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BrokerProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AgentUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: false),
                    TlsCertThumbprint = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSeen = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Approved = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    UseLocalServices = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrokerProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrokerProfiles_UseLocalServices",
                table: "BrokerProfiles",
                column: "UseLocalServices");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrokerProfiles");

            migrationBuilder.DropColumn(
                name: "BrokerId",
                table: "TopicWatchers");

            migrationBuilder.DropColumn(
                name: "BrokerId",
                table: "ScheduledTasks");

            migrationBuilder.DropColumn(
                name: "BrokerId",
                table: "MetricSnapshots");

            migrationBuilder.DropColumn(
                name: "BrokerId",
                table: "Devices");
        }
    }
}
