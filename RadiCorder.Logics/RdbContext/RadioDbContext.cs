using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace RadiCorder.Logics.RdbContext;

public class RadioDbContext : DbContext
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public RadioDbContext(DbContextOptions<RadioDbContext> options)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        : base(options)
    {
    }

    public virtual DbSet<AppConfiguration> AppConfigurations { get; set; }
    public virtual DbSet<Notification> Notification { get; set; }
    public virtual DbSet<RadikoStation> RadikoStations { get; set; }
    public virtual DbSet<RadikoProgram> RadikoPrograms { get; set; }
    public virtual DbSet<NhkRadiruArea> NhkRadiruAreas { get; set; }
    public virtual DbSet<NhkRadiruAreaService> NhkRadiruAreaServices { get; set; }
    public virtual DbSet<NhkRadiruProgram> NhkRadiruPrograms { get; set; }
    public virtual DbSet<ProgramReserve> ProgramReserve { get; set; }
    public virtual DbSet<KeywordReserve> KeywordReserve { get; set; }
    public virtual DbSet<KeywordReserveRadioStation> KeywordReserveRadioStations { get; set; }
    public virtual DbSet<ScheduleJob> ScheduleJob { get; set; }
    public virtual DbSet<ScheduleJobKeywordReserveRelation> ScheduleJobKeywordReserveRelations { get; set; }
    public virtual DbSet<RecordingTag> RecordingTags { get; set; }
    public virtual DbSet<RecordingTagRelation> RecordingTagRelations { get; set; }
    public virtual DbSet<KeywordReserveTagRelation> KeywordReserveTagRelations { get; set; }
    public virtual DbSet<Recording> Recordings { get; set; }
    public virtual DbSet<RecordingFile> RecordingFiles { get; set; }
    public virtual DbSet<RecordingMetadata> RecordingMetadatas { get; set; }

    public override int SaveChanges()
    {
        NormalizeDateTimeOffsetsToUtc();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NormalizeDateTimeOffsetsToUtc();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        NormalizeDateTimeOffsetsToUtc();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        NormalizeDateTimeOffsetsToUtc();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }



    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            var dateTimeOffsetToBinaryConverter = new DateTimeOffsetToBinaryConverter();
            var nullableDateTimeOffsetToBinaryConverter = new ValueConverter<DateTimeOffset?, long?>(
                v => v.HasValue ? v.Value.UtcDateTime.Ticks : null,
                v => v.HasValue ? new DateTimeOffset(new DateTime(v.Value, DateTimeKind.Utc)) : null);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTimeOffset))
                    {
                        property.SetValueConverter(dateTimeOffsetToBinaryConverter);
                    }
                    else if (property.ClrType == typeof(DateTimeOffset?))
                    {
                        property.SetValueConverter(nullableDateTimeOffsetToBinaryConverter);
                    }
                }
            }
        }

        var ulidToStringConverter = new ValueConverter<Ulid, string>(
            v => v.ToString(),
            v => Ulid.Parse(v));

        modelBuilder.Entity<AppConfiguration>(entity =>
        {
            entity.Property(e => e.ConfigurationName)
                .HasColumnOrder(1);
            entity.Property(e => e.Val1)
                .HasColumnOrder(2);
            entity.Property(e => e.Val2)
                .HasColumnOrder(3);
            entity.Property(e => e.Val3)
                .HasColumnOrder(4);
            entity.Property(e => e.Val4)
                .HasColumnOrder(5);

        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.Property(e => e.Id)
                .HasConversion(ulidToStringConverter)
                .HasColumnOrder(1);

            entity.Property(e => e.LogLevel)
                .HasColumnOrder(2);

            entity.Property(e => e.Category)
                .HasColumnOrder(3);

            entity.Property(e => e.Message)
                .HasColumnOrder(4);

            entity.Property(e => e.Timestamp)
                .HasColumnOrder(5);

            entity.Property(e => e.IsRead)
                .HasColumnOrder(6);
        });

        modelBuilder.Entity<RadikoStation>(entity =>
        {
            entity.Property(e => e.StationId)
                .HasColumnOrder(1);

            entity.Property(e => e.RegionId)
                .HasColumnOrder(2);

            entity.Property(e => e.RegionName)
                .HasColumnOrder(3);

            entity.Property(e => e.RegionOrder)
                .HasColumnOrder(4);

            entity.Property(e => e.Area)
                .HasColumnOrder(5);

            entity.Property(e => e.StationName)
                .HasColumnOrder(6);

            entity.Property(e => e.StationUrl)
                .HasColumnOrder(7);

            entity.Property(e => e.LogoPath)
                .HasColumnOrder(8);

            entity.Property(e => e.AreaFree)
                .HasColumnOrder(9);

            entity.Property(e => e.TimeFree)
                .HasColumnOrder(10);

            entity.Property(e => e.StationOrder)
                .HasColumnOrder(11);

            entity.Property(e => e.IsActive)
                .HasColumnOrder(12);

            entity.Property(e => e.LastSeenAtUtc)
                .HasColumnOrder(13);
        });

        modelBuilder.Entity<RadikoProgram>(entity =>
        {
            entity.HasIndex(b => new { Id = b.ProgramId, b.StationId })
                .IsUnique();

            entity.Property(e => e.ProgramId)
                .HasColumnOrder(1);

            entity.Property(e => e.StationId)
                .HasColumnOrder(2);

            entity.Property(e => e.RadioDate)
                .HasColumnOrder(3);

            entity.Property(e => e.DaysOfWeek)
                .HasColumnOrder(4);

            entity.Property(e => e.StartTime)
                .HasColumnOrder(5);

            entity.Property(e => e.EndTime)
                .HasColumnOrder(6);

            entity.Property(e => e.Title)
                .HasColumnOrder(7);

            entity.Property(e => e.Performer)
                .HasColumnOrder(8);

            entity.Property(e => e.Description)
                .HasColumnOrder(9);

            entity.Property(e => e.AvailabilityTimeFree)
                .HasColumnOrder(10);

            entity.Property(e => e.ProgramUrl)
                .HasColumnOrder(11);

            entity.Property(e => e.ImageUrl)
                .HasColumnOrder(12);
        });

        modelBuilder.Entity<NhkRadiruArea>(entity =>
        {
            entity.HasIndex(b => b.AreaId)
                .IsUnique();

            entity.Property(e => e.Id)
                .HasColumnOrder(1);

            entity.Property(e => e.AreaId)
                .HasColumnOrder(2);

            entity.Property(e => e.AreaJpName)
                .HasColumnOrder(3);

            entity.Property(e => e.ApiKey)
                .HasColumnOrder(4);

            entity.Property(e => e.ProgramNowOnAirApiUrl)
                .HasColumnOrder(5);

            entity.Property(e => e.ProgramDetailApiUrlTemplate)
                .HasColumnOrder(6);

            entity.Property(e => e.DailyProgramApiUrlTemplate)
                .HasColumnOrder(7);

            entity.Property(e => e.LastSyncedAtUtc)
                .HasColumnOrder(8);
        });

        modelBuilder.Entity<NhkRadiruAreaService>(entity =>
        {
            entity.HasIndex(b => new { b.AreaId, b.ServiceId })
                .IsUnique();

            entity.HasIndex(b => b.AreaId);
            entity.HasIndex(b => b.ServiceId);

            entity.Property(e => e.Id)
                .HasColumnOrder(1);

            entity.Property(e => e.AreaId)
                .HasColumnOrder(2);

            entity.Property(e => e.ServiceId)
                .HasColumnOrder(3);

            entity.Property(e => e.ServiceName)
                .HasColumnOrder(4);

            entity.Property(e => e.HlsUrl)
                .HasColumnOrder(5);

            entity.Property(e => e.IsActive)
                .HasColumnOrder(6);

            entity.Property(e => e.SourceTag)
                .HasColumnOrder(7);

            entity.Property(e => e.LastSyncedAtUtc)
                .HasColumnOrder(8);

            entity.HasOne<NhkRadiruArea>()
                .WithMany()
                .HasForeignKey(x => x.AreaId)
                .HasPrincipalKey(x => x.AreaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NhkRadiruProgram>(entity =>
        {
            entity.HasIndex(b => new { Id = b.ProgramId, ServiceId = b.StationId, b.AreaId })
                .IsUnique();

            entity.Property(e => e.ProgramId)
                .HasColumnOrder(1);

            entity.Property(e => e.StationId)
                .HasColumnOrder(2);

            entity.Property(e => e.AreaId)
                .HasColumnOrder(3);

            entity.Property(e => e.Title)
                .HasColumnOrder(4);

            entity.Property(e => e.Subtitle)
                .HasColumnOrder(5);

            entity.Property(e => e.RadioDate)
                .HasColumnOrder(6);

            entity.Property(e => e.DaysOfWeek)
                .HasColumnOrder(7);

            entity.Property(e => e.StartTime)
                .HasColumnOrder(8);

            entity.Property(e => e.EndTime)
                .HasColumnOrder(9);

            entity.Property(e => e.Performer)
                .HasColumnOrder(10);

            entity.Property(e => e.Description)
                .HasColumnOrder(11);

            entity.Property(e => e.SiteId)
                .HasColumnOrder(12);

            entity.Property(e => e.EventId)
                .HasColumnOrder(13);

            entity.Property(e => e.ProgramUrl)
                .HasColumnOrder(14);

            entity.Property(e => e.ImageUrl)
                .HasColumnOrder(15);

            entity.Property(e => e.OnDemandContentUrl)
                .HasColumnOrder(16);

            entity.Property(e => e.OnDemandExpiresAtUtc)
                .HasColumnOrder(17);
        });

        modelBuilder.Entity<ProgramReserve>(entity =>
        {
            entity.HasIndex(b => new { b.Id, b.ReservationType })
                .IsUnique();

            entity.Property(e => e.Id)
                .HasConversion(ulidToStringConverter)
                .HasColumnOrder(1);

            entity.Property(e => e.ReservationType)
                .HasColumnOrder(2);

            entity.Property(e => e.RadioServiceKind)
                .HasColumnOrder(3);

            entity.Property(e => e.Name)
                .HasColumnOrder(4);

            entity.Property(e => e.RadioStationId)
                .HasColumnOrder(5);

            entity.Property(e => e.FileName)
                .HasColumnOrder(6);

            entity.Property(e => e.FolderPath)
                .HasColumnOrder(7);

            entity.Property(e => e.StartTime)
                .HasColumnOrder(8);

            entity.Property(e => e.EndTime)
                .HasColumnOrder(9);

            entity.Property(e => e.IsEnable)
                .HasColumnOrder(10);

            entity.Property(e => e.IsTimeFree)
                .HasColumnOrder(11);

            entity.Property(e => e.StartDelay)
                .HasColumnOrder(12);

            entity.Property(e => e.EndDelay)
                .HasColumnOrder(13);

            entity.Property(e => e.ProgramId)
                .HasColumnOrder(14);
        });

        modelBuilder.Entity<KeywordReserve>(entity =>
        {
            entity.HasIndex(b => new { b.Id })
                .IsUnique();

            entity.Property(e => e.Id)
                .HasConversion(ulidToStringConverter)
                .HasColumnOrder(1);

            entity.Property(e => e.Keyword)
                .HasColumnOrder(2);

            entity.Property(e => e.ExcludedKeyword)
                .HasColumnOrder(3);

            entity.Property(e => e.IsTitleOnly)
                .HasColumnOrder(4);

            entity.Property(e => e.IsExcludeTitleOnly)
                .HasColumnOrder(5);

            entity.Property(e => e.FileName)
                .HasColumnOrder(6);

            entity.Property(e => e.FolderPath)
                .HasColumnOrder(7);

            entity.Property(e => e.StartTime)
                .HasColumnOrder(8);

            entity.Property(e => e.EndTime)
                .HasColumnOrder(9);

            entity.Property(e => e.IsEnable)
                .HasColumnOrder(10);

            entity.Property(e => e.DaysOfWeek)
                .HasColumnOrder(11);

            entity.Property(e => e.StartDelay)
                .HasColumnOrder(12);

            entity.Property(e => e.EndDelay)
                .HasColumnOrder(13);

            entity.Property(e => e.SortOrder)
                .HasColumnOrder(14);

            entity.Property(e => e.MergeTagBehavior)
                .HasColumnOrder(15);
        });

        modelBuilder.Entity<KeywordReserveRadioStation>(entity =>
        {
            entity.HasIndex(b => new { b.Id });

            entity.HasKey(k => new { k.Id, k.RadioStation });

            entity.Property(e => e.Id)
                .HasConversion(ulidToStringConverter)
                .HasColumnOrder(1);

            entity.Property(e => e.RadioServiceKind)
                .HasColumnOrder(2);

            entity.Property(e => e.RadioStation)
                .HasColumnOrder(3);
        });

        modelBuilder.Entity<ScheduleJob>(entity =>
        {
            // 実行中ジョブの重複実行を抑止するため、同一番組・同一予約種別・同一キーワード予約の
            // アクティブ状態重複をDB制約で防ぐ。
            entity.HasIndex(e => new { e.ProgramId, e.ServiceKind, e.StartDateTime, e.ReserveType, e.KeywordReserveId })
                .HasDatabaseName("IX_ScheduleJob_Active_Unique")
                .IsUnique()
                .HasFilter($"State IN ({(int)Models.Enums.ScheduleJobState.Pending}, {(int)Models.Enums.ScheduleJobState.Queued}, {(int)Models.Enums.ScheduleJobState.Preparing}, {(int)Models.Enums.ScheduleJobState.Recording})");

            entity.HasIndex(e => new { e.IsEnabled, e.State, e.PrepareStartUtc })
                .HasDatabaseName("IX_ScheduleJob_SchedulerScan");

            entity.Property(e => e.Id)
                .HasConversion(ulidToStringConverter)
                .HasColumnOrder(1);

            entity.Property(e => e.KeywordReserveId)
                .HasConversion(ulidToStringConverter)
                .HasColumnOrder(2);

            entity.Property(e => e.ServiceKind)
                .HasColumnOrder(3);

            entity.Property(e => e.StationId)
                .HasColumnOrder(4);

            entity.Property(e => e.AreaId)
                .HasColumnOrder(5);

            entity.Property(e => e.ProgramId)
                .HasColumnOrder(6);

            entity.Property(e => e.Title)
                .HasColumnOrder(7);

            entity.Property(e => e.Subtitle)
                .HasColumnOrder(8);

            entity.Property(e => e.FilePath)
                .HasColumnOrder(9);

            entity.Property(e => e.StartDateTime)
                .HasColumnOrder(10);

            entity.Property(e => e.EndDateTime)
                .HasColumnOrder(11);

            entity.Property(e => e.StartDelay)
                .HasColumnOrder(12);

            entity.Property(e => e.EndDelay)
                .HasColumnOrder(13);

            entity.Property(e => e.Performer)
                .HasColumnOrder(14);

            entity.Property(e => e.Description)
                .HasColumnOrder(15);

            entity.Property(e => e.RecordingType)
                .HasColumnOrder(16);

            entity.Property(e => e.ReserveType)
                .HasColumnOrder(17);

            entity.Property(e => e.IsEnabled)
                .HasColumnOrder(18);

            entity.Property(e => e.State)
                .HasColumnOrder(19);

            entity.Property(e => e.PrepareStartUtc)
                .HasColumnOrder(20);

            entity.Property(e => e.QueuedAtUtc)
                .HasColumnOrder(21);

            entity.Property(e => e.ActualStartUtc)
                .HasColumnOrder(22);

            entity.Property(e => e.CompletedUtc)
                .HasColumnOrder(23);

            entity.Property(e => e.RetryCount)
                .HasColumnOrder(24);

            entity.Property(e => e.LastErrorCode)
                .HasColumnOrder(25);

            entity.Property(e => e.LastErrorDetail)
                .HasColumnOrder(26);
        });

        modelBuilder.Entity<ScheduleJobKeywordReserveRelation>(entity =>
        {
            entity.HasKey(e => new { e.ScheduleJobId, e.KeywordReserveId });

            entity.Property(e => e.ScheduleJobId)
                .HasConversion(ulidToStringConverter)
                .HasColumnOrder(1);

            entity.Property(e => e.KeywordReserveId)
                .HasConversion(ulidToStringConverter)
                .HasColumnOrder(2);

            entity.HasOne(e => e.ScheduleJob)
                .WithMany(s => s.KeywordReserveRelations)
                .HasForeignKey(e => e.ScheduleJobId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.KeywordReserve)
                .WithMany(k => k.ScheduleJobRelations)
                .HasForeignKey(e => e.KeywordReserveId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecordingTag>(entity =>
        {
            entity.Property(e => e.Id)
                .HasColumnOrder(1);

            entity.Property(e => e.Name)
                .HasColumnOrder(2);

            entity.Property(e => e.NormalizedName)
                .HasColumnOrder(3);

            entity.Property(e => e.CreatedAt)
                .HasColumnOrder(4);

            entity.Property(e => e.UpdatedAt)
                .HasColumnOrder(5);

            entity.Property(e => e.LastUsedAt)
                .HasColumnOrder(6);

            entity.HasIndex(e => e.NormalizedName)
                .IsUnique();
        });

        modelBuilder.Entity<RecordingTagRelation>(entity =>
        {
            entity.HasKey(e => new { e.RecordingId, e.TagId });

            entity.Property(e => e.RecordingId)
                .HasConversion(ulidToStringConverter)
                .HasColumnOrder(1);

            entity.Property(e => e.TagId)
                .HasColumnOrder(2);

            entity.HasOne(e => e.Recording)
                .WithMany()
                .HasForeignKey(fk => fk.RecordingId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Tag)
                .WithMany(r => r.RecordingTagRelations)
                .HasForeignKey(fk => fk.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KeywordReserveTagRelation>(entity =>
        {
            entity.HasKey(e => new { e.ReserveId, e.TagId });

            entity.Property(e => e.ReserveId)
                .HasConversion(ulidToStringConverter)
                .HasColumnOrder(1);

            entity.Property(e => e.TagId)
                .HasColumnOrder(2);

            entity.HasOne(e => e.KeywordReserve)
                .WithMany(r => r.KeywordReserveTagRelations)
                .HasForeignKey(fk => fk.ReserveId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Tag)
                .WithMany(r => r.KeywordReserveTagRelations)
                .HasForeignKey(fk => fk.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Recording>(entity =>
        {
            entity.Property(e => e.Id)
                .HasConversion(ulidToStringConverter)
                .HasColumnOrder(1);

            entity.Property(e => e.ServiceKind)
                .HasColumnOrder(2);

            entity.Property(e => e.ProgramId)
                .HasColumnOrder(3);

            entity.Property(e => e.StationId)
                .HasColumnOrder(4);

            entity.Property(e => e.AreaId)
                .HasColumnOrder(5);

            entity.Property(e => e.StartDateTime)
                .HasColumnOrder(6);

            entity.Property(e => e.EndDateTime)
                .HasColumnOrder(7);

            entity.Property(e => e.IsTimeFree)
                .HasColumnOrder(8);

            entity.Property(e => e.State)
                .HasColumnOrder(9);

            entity.Property(e => e.ErrorMessage)
                .HasColumnOrder(10);

            entity.Property(e => e.CreatedAt)
                .HasColumnOrder(11);

            entity.Property(e => e.UpdatedAt)
                .HasColumnOrder(12);

            entity.Property(e => e.SourceType)
                .HasColumnOrder(13);

            entity.Property(e => e.IsListened)
                .HasColumnOrder(14);

            entity.HasOne(e => e.RecordingFile)
                .WithOne(f => f.Recording)
                .HasForeignKey<RecordingFile>(f => f.RecordingId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.RecordingMetadata)
                .WithOne(m => m.Recording)
                .HasForeignKey<RecordingMetadata>(m => m.RecordingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecordingFile>(entity =>
        {
            entity.Property(e => e.RecordingId)
                .HasConversion(ulidToStringConverter)
                .HasColumnOrder(1);

            entity.Property(e => e.FileRelativePath)
                .HasColumnOrder(2);

            entity.Property(e => e.HasHlsFile)
                .HasColumnOrder(3);

            entity.Property(e => e.HlsDirectoryPath)
                .HasColumnOrder(4);
        });

        modelBuilder.Entity<RecordingMetadata>(entity =>
        {
            entity.Property(e => e.RecordingId)
                .HasConversion(ulidToStringConverter)
                .HasColumnOrder(1);

            entity.Property(e => e.StationName)
                .HasColumnOrder(2);

            entity.Property(e => e.Title)
                .HasColumnOrder(3);

            entity.Property(e => e.Subtitle)
                .HasColumnOrder(4);

            entity.Property(e => e.Performer)
                .HasColumnOrder(5);

            entity.Property(e => e.Description)
                .HasColumnOrder(6);

            entity.Property(e => e.ProgramUrl)
                .HasColumnOrder(7);
        });
    }

    private void NormalizeDateTimeOffsetsToUtc()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            foreach (var property in entry.Properties)
            {
                var clrType = property.Metadata.ClrType;
                if (clrType == typeof(DateTimeOffset) && property.CurrentValue is DateTimeOffset dto)
                {
                    property.CurrentValue = dto.ToUniversalTime();
                    continue;
                }

                if (clrType == typeof(DateTimeOffset?) && property.CurrentValue is DateTimeOffset nullableDto)
                {
                    property.CurrentValue = nullableDto.ToUniversalTime();
                }
            }
        }
    }
}
