using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadiCorder.Logics.RdbContext.Migrations
{
    /// <inheritdoc />
    public partial class AddRadikoStationActiveFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "StationOrder",
                table: "RadikoStations",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER")
                .Annotation("Relational:ColumnOrder", 11);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "RadikoStations",
                type: "INTEGER",
                nullable: false,
                defaultValue: true)
                .Annotation("Relational:ColumnOrder", 12);

            migrationBuilder.AddColumn<long>(
                name: "LastSeenAtUtc",
                table: "RadikoStations",
                type: "INTEGER",
                nullable: true)
                .Annotation("Relational:ColumnOrder", 13);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "RadikoStations");

            migrationBuilder.DropColumn(
                name: "LastSeenAtUtc",
                table: "RadikoStations");

            migrationBuilder.AlterColumn<int>(
                name: "StationOrder",
                table: "RadikoStations",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER")
                .OldAnnotation("Relational:ColumnOrder", 11);
        }
    }
}
