using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadiCorder.Logics.RdbContext.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyNhkRadiruStations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NhkRadiruStations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NhkRadiruStations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AreaId = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    AreaJpName = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    R1Hls = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    R2Hls = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    FmHls = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    ProgramNowOnAirApiUrl = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    ProgramDetailApiUrlTemplate = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    DailyProgramApiUrlTemplate = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NhkRadiruStations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NhkRadiruStations_AreaId_ApiKey",
                table: "NhkRadiruStations",
                columns: new[] { "AreaId", "ApiKey" },
                unique: true);
        }
    }
}
