using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadiCorder.Logics.RdbContext.Migrations
{
    /// <inheritdoc />
    public partial class AddRadiruAreaTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NhkRadiruAreas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AreaId = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    AreaJpName = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    ProgramNowOnAirApiUrl = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    ProgramDetailApiUrlTemplate = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    DailyProgramApiUrlTemplate = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    LastSyncedAtUtc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NhkRadiruAreas", x => x.Id);
                    table.UniqueConstraint("AK_NhkRadiruAreas_AreaId", x => x.AreaId);
                });

            migrationBuilder.CreateTable(
                name: "NhkRadiruAreaServices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AreaId = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ServiceId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ServiceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    HlsUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SourceTag = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LastSyncedAtUtc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NhkRadiruAreaServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NhkRadiruAreaServices_NhkRadiruAreas_AreaId",
                        column: x => x.AreaId,
                        principalTable: "NhkRadiruAreas",
                        principalColumn: "AreaId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NhkRadiruAreas_AreaId",
                table: "NhkRadiruAreas",
                column: "AreaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NhkRadiruAreaServices_AreaId",
                table: "NhkRadiruAreaServices",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_NhkRadiruAreaServices_AreaId_ServiceId",
                table: "NhkRadiruAreaServices",
                columns: new[] { "AreaId", "ServiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NhkRadiruAreaServices_ServiceId",
                table: "NhkRadiruAreaServices",
                column: "ServiceId");

            migrationBuilder.Sql(
                """
                INSERT INTO NhkRadiruAreas
                (
                    AreaId,
                    AreaJpName,
                    ApiKey,
                    ProgramNowOnAirApiUrl,
                    ProgramDetailApiUrlTemplate,
                    DailyProgramApiUrlTemplate,
                    LastSyncedAtUtc
                )
                SELECT
                    AreaId,
                    AreaJpName,
                    ApiKey,
                    ProgramNowOnAirApiUrl,
                    ProgramDetailApiUrlTemplate,
                    DailyProgramApiUrlTemplate,
                    NULL
                FROM NhkRadiruStations
                ;
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO NhkRadiruAreaServices
                (
                    AreaId,
                    ServiceId,
                    ServiceName,
                    HlsUrl,
                    IsActive,
                    SourceTag,
                    LastSyncedAtUtc
                )
                SELECT
                    AreaId,
                    'r1',
                    'NHKラジオ第1',
                    R1Hls,
                    1,
                    'r1hls',
                    NULL
                FROM NhkRadiruStations
                WHERE R1Hls IS NOT NULL AND TRIM(R1Hls) <> ''
                UNION ALL
                SELECT
                    AreaId,
                    'r2',
                    'NHKラジオ第2',
                    R2Hls,
                    1,
                    'r2hls',
                    NULL
                FROM NhkRadiruStations
                WHERE R2Hls IS NOT NULL AND TRIM(R2Hls) <> ''
                UNION ALL
                SELECT
                    AreaId,
                    'r3',
                    'NHK-FM',
                    FmHls,
                    1,
                    'fmhls',
                    NULL
                FROM NhkRadiruStations
                WHERE FmHls IS NOT NULL AND TRIM(FmHls) <> ''
                ;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NhkRadiruAreaServices");

            migrationBuilder.DropTable(
                name: "NhkRadiruAreas");
        }
    }
}
