using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MQTTower.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueAgentUrlAndDeviceStateFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Upgrade safety: remove duplicate non-empty AgentUrl rows before creating the unique index.
            migrationBuilder.Sql(
                """
                DELETE FROM BrokerProfiles
                WHERE Id IN (
                  SELECT Id FROM (
                    SELECT Id, ROW_NUMBER() OVER (PARTITION BY AgentUrl ORDER BY Id) AS rn
                    FROM BrokerProfiles
                    WHERE AgentUrl <> ''
                  )
                  WHERE rn > 1
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_BrokerProfiles_AgentUrl",
                table: "BrokerProfiles",
                column: "AgentUrl",
                unique: true,
                filter: "AgentUrl <> ''");

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceStates_Devices_DeviceId",
                table: "DeviceStates",
                column: "DeviceId",
                principalTable: "Devices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeviceStates_Devices_DeviceId",
                table: "DeviceStates");

            migrationBuilder.DropIndex(
                name: "IX_BrokerProfiles_AgentUrl",
                table: "BrokerProfiles");
        }
    }
}
