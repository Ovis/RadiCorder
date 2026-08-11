using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadiCorder.Logics.RdbContext.Migrations
{
    /// <inheritdoc />
    public partial class Initialize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppConfigurations",
                columns: table => new
                {
                    ConfigurationName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Val1 = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Val2 = table.Column<int>(type: "INTEGER", nullable: true),
                    Val3 = table.Column<decimal>(type: "TEXT", nullable: true),
                    Val4 = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppConfigurations", x => x.ConfigurationName);
                });

            migrationBuilder.CreateTable(
                name: "KeywordReserve",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Keyword = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ExcludedKeyword = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    IsTitleOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsExcludeTitleOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FolderPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    IsEnable = table.Column<bool>(type: "INTEGER", nullable: false),
                    DaysOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    StartDelay = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    EndDelay = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    MergeTagBehavior = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeywordReserve", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KeywordReserveRadioStations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    RadioServiceKind = table.Column<int>(type: "INTEGER", nullable: false),
                    RadioStation = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeywordReserveRadioStations", x => new { x.Id, x.RadioStation });
                });

            migrationBuilder.CreateTable(
                name: "NhkRadiruPrograms",
                columns: table => new
                {
                    ProgramId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StationId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AreaId = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Subtitle = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RadioDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DaysOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    StartTime = table.Column<long>(type: "INTEGER", nullable: false),
                    EndTime = table.Column<long>(type: "INTEGER", nullable: false),
                    Performer = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    SiteId = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    EventId = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ProgramUrl = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    OnDemandContentUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    OnDemandExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NhkRadiruPrograms", x => x.ProgramId);
                });

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

            migrationBuilder.CreateTable(
                name: "Notification",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    LogLevel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Timestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notification", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProgramReserve",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    ReservationType = table.Column<int>(type: "INTEGER", nullable: false),
                    RadioServiceKind = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RadioStationId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FolderPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    IsEnable = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsTimeFree = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartDelay = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    EndDelay = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    ProgramId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TagId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramReserve", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RadikoPrograms",
                columns: table => new
                {
                    ProgramId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    StationId = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    RadioDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DaysOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    StartTime = table.Column<long>(type: "INTEGER", nullable: false),
                    EndTime = table.Column<long>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Performer = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: false),
                    AvailabilityTimeFree = table.Column<int>(type: "INTEGER", nullable: false),
                    ProgramUrl = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadikoPrograms", x => x.ProgramId);
                });

            migrationBuilder.CreateTable(
                name: "RadikoStations",
                columns: table => new
                {
                    StationId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RegionId = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    RegionName = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    RegionOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Area = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    StationName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    StationUrl = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LogoPath = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    AreaFree = table.Column<bool>(type: "INTEGER", nullable: false),
                    TimeFree = table.Column<bool>(type: "INTEGER", nullable: false),
                    StationOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadikoStations", x => x.StationId);
                });

            migrationBuilder.CreateTable(
                name: "Recordings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    ServiceKind = table.Column<int>(type: "INTEGER", nullable: false),
                    ProgramId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StationId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AreaId = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    StartDateTime = table.Column<long>(type: "INTEGER", nullable: false),
                    EndDateTime = table.Column<long>(type: "INTEGER", nullable: false),
                    IsTimeFree = table.Column<bool>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    IsListened = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recordings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecordingTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastUsedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordingTags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleJob",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    KeywordReserveId = table.Column<string>(type: "TEXT", nullable: true),
                    ServiceKind = table.Column<int>(type: "INTEGER", nullable: false),
                    StationId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AreaId = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    ProgramId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Subtitle = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    StartDateTime = table.Column<long>(type: "INTEGER", nullable: false),
                    EndDateTime = table.Column<long>(type: "INTEGER", nullable: false),
                    StartDelay = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    EndDelay = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    Performer = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    RecordingType = table.Column<int>(type: "INTEGER", nullable: false),
                    ReserveType = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    PrepareStartUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    QueuedAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    ActualStartUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    CompletedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastErrorCode = table.Column<int>(type: "INTEGER", nullable: false),
                    LastErrorDetail = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TagId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleJob", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecordingFiles",
                columns: table => new
                {
                    RecordingId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    FileRelativePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    HasHlsFile = table.Column<bool>(type: "INTEGER", nullable: false),
                    HlsDirectoryPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordingFiles", x => x.RecordingId);
                    table.ForeignKey(
                        name: "FK_RecordingFiles_Recordings_RecordingId",
                        column: x => x.RecordingId,
                        principalTable: "Recordings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecordingMetadatas",
                columns: table => new
                {
                    RecordingId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    StationName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Subtitle = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Performer = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    ProgramUrl = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordingMetadatas", x => x.RecordingId);
                    table.ForeignKey(
                        name: "FK_RecordingMetadatas_Recordings_RecordingId",
                        column: x => x.RecordingId,
                        principalTable: "Recordings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KeywordReserveTagRelations",
                columns: table => new
                {
                    ReserveId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TagId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeywordReserveTagRelations", x => new { x.ReserveId, x.TagId });
                    table.ForeignKey(
                        name: "FK_KeywordReserveTagRelations_KeywordReserve_ReserveId",
                        column: x => x.ReserveId,
                        principalTable: "KeywordReserve",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KeywordReserveTagRelations_RecordingTags_TagId",
                        column: x => x.TagId,
                        principalTable: "RecordingTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecordingTagRelations",
                columns: table => new
                {
                    RecordingId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TagId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordingTagRelations", x => new { x.RecordingId, x.TagId });
                    table.ForeignKey(
                        name: "FK_RecordingTagRelations_RecordingTags_TagId",
                        column: x => x.TagId,
                        principalTable: "RecordingTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecordingTagRelations_Recordings_RecordingId",
                        column: x => x.RecordingId,
                        principalTable: "Recordings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleJobKeywordReserveRelations",
                columns: table => new
                {
                    ScheduleJobId = table.Column<string>(type: "TEXT", nullable: false),
                    KeywordReserveId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleJobKeywordReserveRelations", x => new { x.ScheduleJobId, x.KeywordReserveId });
                    table.ForeignKey(
                        name: "FK_ScheduleJobKeywordReserveRelations_KeywordReserve_KeywordReserveId",
                        column: x => x.KeywordReserveId,
                        principalTable: "KeywordReserve",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScheduleJobKeywordReserveRelations_ScheduleJob_ScheduleJobId",
                        column: x => x.ScheduleJobId,
                        principalTable: "ScheduleJob",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KeywordReserve_Id",
                table: "KeywordReserve",
                column: "Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KeywordReserveRadioStations_Id",
                table: "KeywordReserveRadioStations",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_KeywordReserveTagRelations_TagId",
                table: "KeywordReserveTagRelations",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_NhkRadiruPrograms_ProgramId_StationId_AreaId",
                table: "NhkRadiruPrograms",
                columns: new[] { "ProgramId", "StationId", "AreaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NhkRadiruStations_AreaId_ApiKey",
                table: "NhkRadiruStations",
                columns: new[] { "AreaId", "ApiKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProgramReserve_Id_ReservationType",
                table: "ProgramReserve",
                columns: new[] { "Id", "ReservationType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RadikoPrograms_ProgramId_StationId",
                table: "RadikoPrograms",
                columns: new[] { "ProgramId", "StationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecordingTagRelations_TagId",
                table: "RecordingTagRelations",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_RecordingTags_NormalizedName",
                table: "RecordingTags",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleJob_Active_Unique",
                table: "ScheduleJob",
                columns: new[] { "ProgramId", "ServiceKind", "StartDateTime", "ReserveType", "KeywordReserveId" },
                unique: true,
                filter: "State IN (0, 1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleJob_SchedulerScan",
                table: "ScheduleJob",
                columns: new[] { "IsEnabled", "State", "PrepareStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleJobKeywordReserveRelations_KeywordReserveId",
                table: "ScheduleJobKeywordReserveRelations",
                column: "KeywordReserveId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppConfigurations");

            migrationBuilder.DropTable(
                name: "KeywordReserveRadioStations");

            migrationBuilder.DropTable(
                name: "KeywordReserveTagRelations");

            migrationBuilder.DropTable(
                name: "NhkRadiruPrograms");

            migrationBuilder.DropTable(
                name: "NhkRadiruStations");

            migrationBuilder.DropTable(
                name: "Notification");

            migrationBuilder.DropTable(
                name: "ProgramReserve");

            migrationBuilder.DropTable(
                name: "RadikoPrograms");

            migrationBuilder.DropTable(
                name: "RadikoStations");

            migrationBuilder.DropTable(
                name: "RecordingFiles");

            migrationBuilder.DropTable(
                name: "RecordingMetadatas");

            migrationBuilder.DropTable(
                name: "RecordingTagRelations");

            migrationBuilder.DropTable(
                name: "ScheduleJobKeywordReserveRelations");

            migrationBuilder.DropTable(
                name: "RecordingTags");

            migrationBuilder.DropTable(
                name: "Recordings");

            migrationBuilder.DropTable(
                name: "KeywordReserve");

            migrationBuilder.DropTable(
                name: "ScheduleJob");
        }
    }
}
