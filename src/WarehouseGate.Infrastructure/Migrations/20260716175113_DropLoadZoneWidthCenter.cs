using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropLoadZoneWidthCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // LoadZoneWidth.Center is being removed from the enum (ZoneWidth column is a
            // .HasConversion<string>(), so any existing "Center" row would throw on read once
            // the enum member is gone). Backfill using the row's own stored geometry: Left if
            // the group's horizontal center sits closer to the vehicle's left wall, else Right.
            migrationBuilder.Sql(@"
                UPDATE g
                SET g.ZoneWidth = CASE
                    WHEN (g.PositionXCm + g.DimXCm / 2.0) <= (v.WidthCm / 2.0) THEN 'Left'
                    ELSE 'Right'
                END
                FROM OutwardLoadPlanGroups g
                JOIN OutwardLoadPlanOptions o ON g.OutwardLoadPlanOptionId = o.Id
                JOIN OutwardTransactions t ON o.OutwardTransactionId = t.Id
                JOIN Vehicles v ON t.VehicleId = v.Id
                WHERE g.ZoneWidth = 'Center';

                -- Fallback for the rare row whose transaction has no vehicle on file: default Left.
                UPDATE OutwardLoadPlanGroups SET ZoneWidth = 'Left' WHERE ZoneWidth = 'Center';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data loss is not reversible (original Center rows are gone), so Down is a no-op -
            // matches the intent (Center is permanently retired from the domain).
        }
    }
}
