using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MQTTower.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackfillMetricBrokerIdNulls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE MetricSnapshots SET BrokerId = 'a1b2c3d4-e5f6-4789-a012-3456789abcde' WHERE BrokerId IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
