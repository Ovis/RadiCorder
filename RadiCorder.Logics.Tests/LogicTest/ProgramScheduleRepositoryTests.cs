using Microsoft.EntityFrameworkCore;
using Moq;
using RadiCorder.Logics.BackgroundServices;
using RadiCorder.Logics.Infrastructure.ProgramSchedule;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Models.Radiko;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Tests.LogicTest;

/// <summary>
/// ProgramScheduleRepositoryのテスト
/// </summary>
public class ProgramScheduleRepositoryTests : UnitTestBase
{
    private RadioDbContext _dbContext = null!;
    private ProgramScheduleRepository _repository = null!;

    [TestCase(false)]
    [TestCase(true)]
    public async Task NHK番組IDの別エリア衝突をDB更新前に検出する(bool sameBatch)
    {
        var now = DateTimeOffset.UtcNow;
        var original = new NhkRadiruProgram { ProgramId = "shared", AreaId = "130", StationId = "r1", Title = "前回", StartTime = now, EndTime = now.AddHours(1) };
        if (!sameBatch) await _repository.UpsertRadiruProgramsAsync([original]);
        var conflicting = new NhkRadiruProgram { ProgramId = "shared", AreaId = "270", StationId = "r1", Title = "別エリア", StartTime = now, EndTime = now.AddHours(1) };
        var unrelated = new NhkRadiruProgram { ProgramId = "other", AreaId = "130", StationId = "r1", Title = "追加", StartTime = now, EndTime = now.AddHours(1) };
        var input = sameBatch ? new[] { original, conflicting, unrelated } : new[] { conflicting, unrelated };
        Assert.ThrowsAsync<RadiCorder.Logics.Errors.DomainException>(async () => await _repository.UpsertRadiruProgramsAsync(input));
        var stored = await _dbContext.NhkRadiruPrograms.AsNoTracking().ToListAsync();
        Assert.That(stored.Count, Is.EqualTo(sameBatch ? 0 : 1));
        if (!sameBatch) Assert.That(stored.Single().Title, Is.EqualTo("前回"));
    }

    [SetUp]
    public async Task Setup()
    {
        _dbContext = DbContext;
        _dbContext.ChangeTracker.Clear();
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM RadikoPrograms");
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM RadikoStations");
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM NhkRadiruPrograms");
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM ScheduleJob");
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM ProgramReserve");
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM AppConfigurations");
        _repository = new ProgramScheduleRepository(_dbContext);
    }

    /// <summary>
    /// 放送中のradiko番組を取得できる
    /// </summary>
    [Test]
    public async Task GetRadikoNowOnAirAsync_放送中だけ取得()
    {
        var now = DateTimeOffset.UtcNow;
        await AddRadikoProgramAsync("P1", "TBS", now.AddMinutes(-5), now.AddMinutes(5));
        await AddRadikoProgramAsync("P2", "TBS", now.AddHours(-2), now.AddHours(-1));

        var list = await _repository.GetRadikoNowOnAirAsync(now);

        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(list[0].ProgramId, Is.EqualTo("P1"));
    }

    /// <summary>
    /// radiko番組一覧取得ができる
    /// </summary>
    [Test]
    public async Task GetRadikoProgramsAsync_日付局で取得()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        await AddRadikoProgramAsync("P1", "TBS", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30), date: date);
        await AddRadikoProgramAsync("P2", "ABC", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30), date: date);

        var list = await _repository.GetRadikoProgramsAsync(date, "TBS");

        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(list[0].StationId, Is.EqualTo("TBS"));
    }

    /// <summary>
    /// radiko番組IDで取得できる
    /// </summary>
    [Test]
    public async Task GetRadikoProgramByIdAsync_取得できる()
    {
        await AddRadikoProgramAsync("P100", "TBS", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30));

        var program = await _repository.GetRadikoProgramByIdAsync("P100");

        Assert.That(program, Is.Not.Null);
        Assert.That(program!.ProgramId, Is.EqualTo("P100"));
    }

    /// <summary>
    /// 番組更新対象のradiko局IDは有効局のみ取得する
    /// </summary>
    [Test]
    public async Task GetRadikoStationIdsAsync_有効局のみ取得()
    {
        _dbContext.RadikoStations.AddRange(
            new RadikoStation { StationId = "TBS", RegionId = "JP13", IsActive = true },
            new RadikoStation { StationId = "OLD", RegionId = "JP13", IsActive = false });
        await _dbContext.SaveChangesAsync();

        var stationIds = await _repository.GetRadikoStationIdsAsync();

        Assert.That(stationIds, Is.EqualTo(new[] { "TBS" }));
    }

    /// <summary>
    /// 全局番組表判定は有効局のみを対象にする
    /// </summary>
    [Test]
    public async Task HasRadikoProgramsForAllStationsThroughAsync_無効局は判定対象外()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _dbContext.RadikoStations.AddRange(
            new RadikoStation { StationId = "TBS", RegionId = "JP13", IsActive = true },
            new RadikoStation { StationId = "OLD", RegionId = "JP13", IsActive = false });
        await _dbContext.SaveChangesAsync();

        await AddRadikoProgramAsync("P1", "TBS", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30), date: today);

        var hasAll = await _repository.HasRadikoProgramsForAllStationsThroughAsync(today);

        Assert.That(hasAll, Is.True);
    }

    /// <summary>
    /// radiko番組の重複追加を避ける
    /// </summary>
    [Test]
    public async Task AddRadikoProgramsIfMissingAsync_重複回避()
    {
        var existing = await AddRadikoProgramAsync("P1", "TBS", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30));
        var newProgram = CreateRadikoProgram("P2", "TBS", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30));

        await _repository.AddRadikoProgramsIfMissingAsync([existing, newProgram]);

        var count = await _dbContext.RadikoPrograms.CountAsync();
        Assert.That(count, Is.EqualTo(2));
    }

    /// <summary>
    /// radiko番組更新時に入力側で同一IDが重複していても更新できる
    /// </summary>
    [Test]
    public async Task AddRadikoProgramsIfMissingAsync_入力に同一ID重複があっても更新できる()
    {
        await AddRadikoProgramAsync("P1", "TBS", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30), title: "Old");

        var updatedA = CreateRadikoProgram("P1", "TBS", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30), title: "NewA");
        var updatedB = CreateRadikoProgram("P1", "TBS", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30), title: "NewB");

        Assert.DoesNotThrowAsync(async () => await _repository.AddRadikoProgramsIfMissingAsync([updatedA, updatedB]));

        var result = await _repository.GetRadikoProgramByIdAsync("P1");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Title, Is.EqualTo("NewB"));
    }

    [Test]
    public async Task 終了日時訂正と番組名更新を同じ番組へ反映し予約と履歴のIDを維持する()
    {
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(2).AddHours(12), TimeSpan.Zero);
        var originalId = "ABC_2026100603000020261006030000";
        await AddRadikoProgramAsync(originalId, "ABC", start, start.AddMinutes(80), title: "推定番組");
        var jobId = Ulid.NewUlid();
        _dbContext.ScheduleJob.Add(new ScheduleJob
        {
            Id = jobId, ServiceKind = RadioServiceKind.Radiko, StationId = "ABC", ProgramId = originalId,
            Title = "推定番組", StartDateTime = start, EndDateTime = start.AddMinutes(80),
            RecordingType = RecordingType.TimeFree, ReserveType = ReserveType.Keyword, State = ScheduleJobState.Pending,
            PrepareStartUtc = start.AddMinutes(83).AddSeconds(-10), IsEnabled = true,
            FilePath = "custom", StartDelay = TimeSpan.FromSeconds(15), EndDelay = TimeSpan.FromSeconds(20)
        });
        _dbContext.ProgramReserve.Add(new ProgramReserve { Id = Ulid.NewUlid(), RadioServiceKind = RadioServiceKind.Radiko, ProgramId = originalId });
        var recordingId = Ulid.NewUlid();
        _dbContext.Recordings.Add(new Recording
        {
            Id = recordingId, ServiceKind = RadioServiceKind.Radiko, StationId = "ABC", ProgramId = originalId,
            StartDateTime = start, EndDateTime = start.AddMinutes(80)
        });
        await _dbContext.SaveChangesAsync();
        var wakeup = new Mock<IRecordingScheduleWakeup>();
        var repository = new ProgramScheduleRepository(_dbContext, wakeup.Object);

        var corrected = CreateRadikoProgram("ABC_corrected", "ABC", start, start.AddMinutes(60), title: "確定番組", description: "更新された説明");
        corrected.Performer = "出演者";
        await repository.AddRadikoProgramsIfMissingAsync([corrected]);
        await repository.AddRadikoProgramsIfMissingAsync([corrected]);

        var stored = (await _dbContext.RadikoPrograms.AsNoTracking().ToListAsync()).Single();
        Assert.That(stored.ProgramId, Is.EqualTo(originalId));
        Assert.That(stored.EndTime, Is.EqualTo(start.AddMinutes(60)));
        Assert.That(stored.Title, Is.EqualTo("確定番組"));
        Assert.That(corrected.ProgramId, Is.EqualTo("ABC_corrected"));
        var job = await _dbContext.ScheduleJob.AsNoTracking().SingleAsync(j => j.Id == jobId);
        Assert.That(job.ProgramId, Is.EqualTo(originalId));
        Assert.That(job.EndDateTime, Is.EqualTo(stored.EndTime));
        Assert.That(job.Title, Is.EqualTo(stored.Title));
        Assert.That(job.Performer, Is.EqualTo(stored.Performer));
        Assert.That(job.Description, Is.EqualTo(stored.Description));
        Assert.That(job.PrepareStartUtc, Is.EqualTo(start.AddMinutes(63).AddSeconds(-10)));
        Assert.That(job.State, Is.EqualTo(ScheduleJobState.Pending));
        Assert.That(job.FilePath, Is.EqualTo("custom"));
        Assert.That(job.StartDelay, Is.EqualTo(TimeSpan.FromSeconds(15)));
        Assert.That(job.EndDelay, Is.EqualTo(TimeSpan.FromSeconds(20)));
        Assert.That((await _dbContext.ProgramReserve.AsNoTracking().SingleAsync()).ProgramId, Is.EqualTo(originalId));
        var recording = await _dbContext.Recordings.AsNoTracking().SingleAsync(r => r.Id == recordingId);
        Assert.That(recording.ProgramId, Is.EqualTo(originalId));
        Assert.That(recording.EndDateTime, Is.EqualTo(start.AddMinutes(80)));
        wakeup.Verify(w => w.Wake(), Times.Once);
    }

    [TestCase(ScheduleJobState.Pending, true)]
    [TestCase(ScheduleJobState.Pending, false)]
    [TestCase(ScheduleJobState.Queued, true)]
    [TestCase(ScheduleJobState.Preparing, true)]
    [TestCase(ScheduleJobState.Recording, true)]
    [TestCase(ScheduleJobState.Finalizing, true)]
    [TestCase(ScheduleJobState.Completed, true)]
    [TestCase(ScheduleJobState.Failed, true)]
    [TestCase(ScheduleJobState.Cancelled, false)]
    public async Task 番組表訂正は待機中の予約だけに反映し実行状態と有効設定を変えない(ScheduleJobState state, bool enabled)
    {
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1).AddHours(12), TimeSpan.Zero);
        await AddRadikoProgramAsync("original", "CRT", start, start.AddMinutes(15), title: "");
        var jobId = Ulid.NewUlid();
        var prepare = start.AddSeconds(-25);
        _dbContext.ScheduleJob.Add(new ScheduleJob
        {
            Id = jobId, ServiceKind = RadioServiceKind.Radiko, StationId = "CRT", ProgramId = "original",
            Title = "", StartDateTime = start, EndDateTime = start.AddMinutes(15), PrepareStartUtc = prepare,
            RecordingType = RecordingType.RealTime, ReserveType = ReserveType.Program,
            State = state, IsEnabled = enabled, ActualStartUtc = state == ScheduleJobState.Recording ? start : null
        });
        await _dbContext.SaveChangesAsync();
        await _repository.AddRadikoProgramsIfMissingAsync([
            CreateRadikoProgram("corrected", "CRT", start, start.AddMinutes(30), title: "新しい番組名")]);
        var job = await _dbContext.ScheduleJob.AsNoTracking().SingleAsync(j => j.Id == jobId);
        Assert.That(job.ProgramId, Is.EqualTo("original"));
        Assert.That(job.Title, Is.EqualTo(state == ScheduleJobState.Pending ? "新しい番組名" : ""));
        Assert.That(job.EndDateTime, Is.EqualTo(start.AddMinutes(state == ScheduleJobState.Pending ? 30 : 15)));
        Assert.That(job.State, Is.EqualTo(state));
        Assert.That(job.IsEnabled, Is.EqualTo(enabled));
        Assert.That(job.PrepareStartUtc, Is.EqualTo(prepare));
        Assert.That(job.ActualStartUtc, Is.EqualTo(state == ScheduleJobState.Recording ? start : (DateTimeOffset?)null));
    }

    [Test]
    public async Task 番組表訂正を別サービス別局と時刻指定予約へ適用しない()
    {
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1).AddHours(12), TimeSpan.Zero);
        await AddRadikoProgramAsync("shared", "ABC", start, start.AddHours(1));
        _dbContext.ScheduleJob.AddRange(
            new ScheduleJob { Id = Ulid.NewUlid(), ServiceKind = RadioServiceKind.Radiru, StationId = "ABC", ProgramId = "shared", Title = "NHK", ReserveType = ReserveType.Program },
            new ScheduleJob { Id = Ulid.NewUlid(), ServiceKind = RadioServiceKind.Radiko, StationId = "CRT", ProgramId = "shared", Title = "別局", ReserveType = ReserveType.Program },
            new ScheduleJob { Id = Ulid.NewUlid(), ServiceKind = RadioServiceKind.Radiko, StationId = "ABC", ProgramId = "shared", Title = "時刻指定", ReserveType = ReserveType.Undefined });
        await _dbContext.SaveChangesAsync();
        await _repository.AddRadikoProgramsIfMissingAsync([CreateRadikoProgram("new", "ABC", start, start.AddHours(2), title: "更新")]);
        Assert.That((await _dbContext.ScheduleJob.AsNoTracking().ToListAsync()).Select(j => j.Title), Is.EquivalentTo(new[] { "NHK", "別局", "時刻指定" }));
    }

    [Test]
    public async Task DB保存失敗時は番組と予約の訂正を一緒にロールバックし次の同期へ持ち越さない()
    {
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1).AddHours(12), TimeSpan.Zero);
        await AddRadikoProgramAsync("original", "ABC", start, start.AddHours(1), title: "元の番組");
        _dbContext.ScheduleJob.Add(new ScheduleJob
        {
            Id = Ulid.NewUlid(), ServiceKind = RadioServiceKind.Radiko, StationId = "ABC", ProgramId = "original",
            Title = "元の番組", StartDateTime = start, EndDateTime = start.AddHours(1),
            RecordingType = RecordingType.RealTime, ReserveType = ReserveType.Program
        });
        await _dbContext.SaveChangesAsync();
        var wakeup = new Mock<IRecordingScheduleWakeup>();
        var repository = new ProgramScheduleRepository(_dbContext, wakeup.Object);
        await _dbContext.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_program_correction BEFORE UPDATE ON RadikoPrograms WHEN NEW.Title = 'fail' BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        try
        {
            Assert.ThrowsAsync<DbUpdateException>(async () => await repository.AddRadikoProgramsIfMissingAsync([
                CreateRadikoProgram("new-id", "ABC", start, start.AddHours(2), title: "fail")]));
        }
        finally { await _dbContext.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_program_correction;"); }

        Assert.That((await _dbContext.RadikoPrograms.AsNoTracking().SingleAsync()).Title, Is.EqualTo("元の番組"));
        Assert.That((await _dbContext.ScheduleJob.AsNoTracking().SingleAsync()).Title, Is.EqualTo("元の番組"));
        wakeup.Verify(w => w.Wake(), Times.Never);
        await repository.AddRadikoProgramsIfMissingAsync([CreateRadikoProgram("new-id", "ABC", start, start.AddHours(2), title: "再取得")]);
        Assert.That((await _dbContext.RadikoPrograms.AsNoTracking().SingleAsync()).Title, Is.EqualTo("再取得"));
        Assert.That((await _dbContext.ScheduleJob.AsNoTracking().SingleAsync()).Title, Is.EqualTo("再取得"));
    }

    [Test]
    public async Task 空の取得結果は既存番組を削除せず開始時刻が異なる新番組は追加する()
    {
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1).AddHours(12), TimeSpan.Zero);
        await AddRadikoProgramAsync("original", "RKK", start, start.AddHours(1));
        await _repository.AddRadikoProgramsIfMissingAsync([]);
        await _repository.AddRadikoProgramsIfMissingAsync([CreateRadikoProgram("next", "RKK", start.AddHours(1), start.AddHours(2))]);
        Assert.That(await _dbContext.RadikoPrograms.CountAsync(), Is.EqualTo(2));
        Assert.That(await _repository.GetRadikoProgramByIdAsync("original"), Is.Not.Null);
    }

    [Test]
    public async Task 同時刻の別局を統合せず同じ局の同時刻データは最新の一件にする()
    {
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1).AddHours(12), TimeSpan.Zero);
        await _repository.AddRadikoProgramsIfMissingAsync([
            CreateRadikoProgram("abc-first", "ABC", start, start.AddHours(1), title: "前の内容"),
            CreateRadikoProgram("crt", "CRT", start, start.AddHours(1), title: "別局"),
            CreateRadikoProgram("abc-last", "ABC", start, start.AddHours(2), title: "最新の内容")]);
        var programs = await _dbContext.RadikoPrograms.AsNoTracking().ToListAsync();
        Assert.That(programs.Count, Is.EqualTo(2));
        Assert.That(programs.Single(p => p.StationId == "ABC").Title, Is.EqualTo("最新の内容"));
        Assert.That(programs.Single(p => p.StationId == "CRT").Title, Is.EqualTo("別局"));
    }

    /// <summary>
    /// radiko番組検索ができる
    /// </summary>
    [Test]
    public async Task SearchRadikoProgramsAsync_キーワード検索()
    {
        var now = DateTimeOffset.UtcNow;
        await AddRadikoProgramAsync("P1", "TBS", now.AddMinutes(-10), now.AddMinutes(10), title: "Alpha", description: "desc");
        await AddRadikoProgramAsync("P2", "TBS", now.AddMinutes(-10), now.AddMinutes(10), title: "Beta", description: "NG");

        var entity = new ProgramSearchEntity
        {
            Keyword = "Alpha",
            SearchTitleOnly = true,
            ExcludedKeyword = "NG",
            SearchTitleOnlyExcludedKeyword = false,
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            IncludeHistoricalPrograms = false
        };

        var list = await _repository.SearchRadikoProgramsAsync(entity, now);

        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(list[0].ProgramId, Is.EqualTo("P1"));
    }

    /// <summary>
    /// radiko番組検索で除外キーワードはいずれか一致で除外される
    /// </summary>
    [Test]
    public async Task SearchRadikoProgramsAsync_除外キーワード複数は一語一致でも除外()
    {
        var now = DateTimeOffset.UtcNow;
        await AddRadikoProgramAsync("P1", "TBS", now.AddMinutes(-10), now.AddMinutes(10), title: "Alpha", description: "contains NG1");
        await AddRadikoProgramAsync("P2", "TBS", now.AddMinutes(-10), now.AddMinutes(10), title: "Beta", description: "safe");

        var entity = new ProgramSearchEntity
        {
            ExcludedKeyword = "NG1 NG2",
            SearchTitleOnlyExcludedKeyword = false,
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            IncludeHistoricalPrograms = false
        };

        var list = await _repository.SearchRadikoProgramsAsync(entity, now);

        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(list[0].ProgramId, Is.EqualTo("P2"));
    }

    /// <summary>
    /// radiko番組検索で録音可能な番組のみ抽出できる
    /// </summary>
    [Test]
    public async Task SearchRadikoProgramsAsync_録音可能のみ抽出()
    {
        var now = new DateTimeOffset(2026, 2, 11, 12, 0, 0, TimeSpan.FromHours(9));
        await AddRadikoProgramAsync(
            "P1",
            "TBS",
            now.AddHours(-2),
            now.AddHours(-1),
            title: "Ended",
            description: "desc",
            availabilityTimeFree: AvailabilityTimeFree.Unavailable);
        await AddRadikoProgramAsync(
            "P2",
            "TBS",
            now.AddHours(-4),
            now.AddHours(-3),
            title: "TimeFree",
            description: "desc",
            availabilityTimeFree: AvailabilityTimeFree.Available);
        await AddRadikoProgramAsync(
            "P3",
            "TBS",
            now.AddMinutes(-10),
            now.AddMinutes(20),
            title: "Live",
            description: "desc",
            availabilityTimeFree: AvailabilityTimeFree.Unavailable);

        var entity = new ProgramSearchEntity
        {
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            IncludeHistoricalPrograms = true,
            RecordableOnly = true
        };

        var list = await _repository.SearchRadikoProgramsAsync(entity, now);

        Assert.That(list.Select(x => x.ProgramId), Is.EqualTo(new[] { "P2", "P3" }));
    }

    /// <summary>
    /// radiko番組検索の時刻指定はローカル時刻で判定する
    /// </summary>
    [Test]
    public async Task SearchRadikoProgramsAsync_時刻指定はJSTで判定()
    {
        var now = new DateTimeOffset(2026, 2, 11, 3, 0, 0, TimeSpan.Zero);
        await AddRadikoProgramAsync(
            "P1",
            "TBS",
            new DateTimeOffset(2026, 2, 12, 9, 15, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 2, 12, 9, 45, 0, TimeSpan.FromHours(9)),
            title: "Morning");
        await AddRadikoProgramAsync(
            "P2",
            "TBS",
            new DateTimeOffset(2026, 2, 12, 18, 15, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 2, 12, 18, 45, 0, TimeSpan.FromHours(9)),
            title: "Evening");

        var entity = new ProgramSearchEntity
        {
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            IncludeHistoricalPrograms = false
        };

        var list = await _repository.SearchRadikoProgramsAsync(entity, now);

        Assert.That(list.Select(x => x.ProgramId), Is.EqualTo(new[] { "P1" }));
    }

    /// <summary>
    /// radiko番組検索の日跨ぎ時刻指定を判定できる
    /// </summary>
    [Test]
    public async Task SearchRadikoProgramsAsync_日跨ぎ時刻指定を判定()
    {
        var now = new DateTimeOffset(2026, 2, 11, 12, 0, 0, TimeSpan.FromHours(9));
        await AddRadikoProgramAsync(
            "P1",
            "TBS",
            new DateTimeOffset(2026, 2, 11, 23, 30, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 2, 12, 0, 30, 0, TimeSpan.FromHours(9)),
            title: "Midnight1");
        await AddRadikoProgramAsync(
            "P2",
            "TBS",
            new DateTimeOffset(2026, 2, 11, 23, 30, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 2, 12, 0, 0, 0, TimeSpan.FromHours(9)),
            title: "Midnight2");
        await AddRadikoProgramAsync(
            "P3",
            "TBS",
            new DateTimeOffset(2026, 2, 11, 22, 30, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 2, 11, 23, 30, 0, TimeSpan.FromHours(9)),
            title: "OutsideStart");
        await AddRadikoProgramAsync(
            "P4",
            "TBS",
            new DateTimeOffset(2026, 2, 12, 0, 30, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 2, 12, 1, 30, 0, TimeSpan.FromHours(9)),
            title: "OutsideEnd");

        var entity = new ProgramSearchEntity
        {
            StartTime = new TimeOnly(23, 0),
            EndTime = new TimeOnly(1, 0),
            IncludeHistoricalPrograms = true
        };

        var list = await _repository.SearchRadikoProgramsAsync(entity, now);

        Assert.That(list.Select(x => x.ProgramId), Is.EquivalentTo(new[] { "P1", "P2" }));
    }

    /// <summary>
    /// radiko番組検索で全日指定(00:00-23:59)は日跨ぎ番組を含める
    /// </summary>
    [Test]
    public async Task SearchRadikoProgramsAsync_全日指定は日跨ぎ番組を含める()
    {
        var now = new DateTimeOffset(2026, 2, 11, 12, 0, 0, TimeSpan.FromHours(9));
        await AddRadikoProgramAsync(
            "P1",
            "TBS",
            new DateTimeOffset(2026, 2, 10, 23, 30, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 2, 11, 0, 30, 0, TimeSpan.FromHours(9)),
            title: "Overnight");

        var entity = new ProgramSearchEntity
        {
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            IncludeHistoricalPrograms = true
        };

        var list = await _repository.SearchRadikoProgramsAsync(entity, now);

        Assert.That(list.Select(x => x.ProgramId), Is.EquivalentTo(new[] { "P1" }));
    }

    /// <summary>
    /// 古いradiko番組を削除できる
    /// </summary>
    [Test]
    public async Task DeleteOldRadikoProgramsAsync_削除()
    {
        var oldDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2));
        await AddRadikoProgramAsync("P1", "TBS", DateTimeOffset.UtcNow.AddMonths(-2), DateTimeOffset.UtcNow.AddMonths(-2).AddMinutes(10), date: oldDate);

        await _repository.DeleteOldRadikoProgramsAsync(DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)));

        var count = await _dbContext.RadikoPrograms.CountAsync();
        Assert.That(count, Is.EqualTo(0));
    }

    /// <summary>
    /// らじる★らじる番組を追加/更新できる
    /// </summary>
    [Test]
    public async Task UpsertRadiruProgramsAsync_追加更新()
    {
        var program = CreateRadiruProgram("R1_1", "JP13", "r1", "TitleA");
        await _repository.UpsertRadiruProgramsAsync([program]);

        _dbContext.ChangeTracker.Clear();

        program.Title = "TitleB";
        await _repository.UpsertRadiruProgramsAsync([program]);

        var result = await _repository.GetRadiruProgramByIdAsync("R1_1");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Title, Is.EqualTo("TitleB"));
    }

    /// <summary>
    /// らじる★らじる番組検索ができる
    /// </summary>
    [Test]
    public async Task SearchRadiruProgramsAsync_キーワード検索()
    {
        var now = DateTimeOffset.UtcNow;
        await AddRadiruProgramAsync("R1_1", "JP13", "r1", now.AddMinutes(-10), now.AddMinutes(10), title: "Alpha");
        await AddRadiruProgramAsync("R1_2", "JP13", "r1", now.AddMinutes(-10), now.AddMinutes(10), title: "Beta", description: "NG");

        var entity = new ProgramSearchEntity
        {
            SelectedRadiruStationIds = ["JP13:r1"],
            Keyword = "Alpha",
            SearchTitleOnly = true,
            ExcludedKeyword = "NG",
            SearchTitleOnlyExcludedKeyword = false,
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            IncludeHistoricalPrograms = false
        };

        var list = await _repository.SearchRadiruProgramsAsync(entity, now);

        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(list[0].ProgramId, Is.EqualTo("R1_1"));
    }

    /// <summary>
    /// らじる★らじる検索で除外キーワードはいずれか一致で除外される
    /// </summary>
    [Test]
    public async Task SearchRadiruProgramsAsync_除外キーワード複数は一語一致でも除外()
    {
        var now = DateTimeOffset.UtcNow;
        await AddRadiruProgramAsync("R1_1", "JP13", "r1", now.AddMinutes(-10), now.AddMinutes(10), title: "Alpha", description: "contains NG1");
        await AddRadiruProgramAsync("R1_2", "JP13", "r1", now.AddMinutes(-10), now.AddMinutes(10), title: "Beta", description: "safe");

        var entity = new ProgramSearchEntity
        {
            SelectedRadiruStationIds = ["JP13:r1"],
            ExcludedKeyword = "NG1 NG2",
            SearchTitleOnlyExcludedKeyword = false,
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            IncludeHistoricalPrograms = false
        };

        var list = await _repository.SearchRadiruProgramsAsync(entity, now);

        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(list[0].ProgramId, Is.EqualTo("R1_2"));
    }

    /// <summary>
    /// らじる★らじる検索で放送済み番組を除外できる
    /// </summary>
    [Test]
    public async Task SearchRadiruProgramsAsync_放送済み除外()
    {
        var now = new DateTimeOffset(2026, 2, 11, 12, 0, 0, TimeSpan.FromHours(9));
        await AddRadiruProgramAsync("R1_1", "JP13", "r1", now.AddHours(-2), now.AddHours(-1), title: "Ended");
        await AddRadiruProgramAsync("R1_2", "JP13", "r1", now.AddMinutes(-10), now.AddMinutes(20), title: "Now");

        var entity = new ProgramSearchEntity
        {
            SelectedRadiruStationIds = ["JP13:r1"],
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            IncludeHistoricalPrograms = false
        };

        var list = await _repository.SearchRadiruProgramsAsync(entity, now);

        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(list[0].ProgramId, Is.EqualTo("R1_2"));
    }

    /// <summary>
    /// らじる★らじる検索で録音可能な番組のみ抽出できる
    /// </summary>
    [Test]
    public async Task SearchRadiruProgramsAsync_録音可能のみ抽出()
    {
        var now = new DateTimeOffset(2026, 2, 11, 12, 0, 0, TimeSpan.FromHours(9));
        await AddRadiruProgramAsync("R1_1", "JP13", "r1", now.AddHours(-2), now.AddHours(-1), title: "Ended");
        await AddRadiruProgramAsync(
            "R1_2",
            "JP13",
            "r1",
            now.AddHours(-3),
            now.AddHours(-2),
            title: "OnDemand",
            onDemandContentUrl: "https://example.com/stream.m3u8",
            onDemandExpiresAtUtc: now.UtcDateTime.AddHours(1));
        await AddRadiruProgramAsync("R1_3", "JP13", "r1", now.AddMinutes(-10), now.AddMinutes(20), title: "Live");

        var entity = new ProgramSearchEntity
        {
            SelectedRadiruStationIds = ["JP13:r1"],
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            IncludeHistoricalPrograms = true,
            RecordableOnly = true
        };

        var list = await _repository.SearchRadiruProgramsAsync(entity, now);

        Assert.That(list.Select(x => x.ProgramId), Is.EqualTo(new[] { "R1_2", "R1_3" }));
    }

    /// <summary>
    /// らじる★らじる検索の日跨ぎ時刻指定を判定できる
    /// </summary>
    [Test]
    public async Task SearchRadiruProgramsAsync_日跨ぎ時刻指定を判定()
    {
        var now = new DateTimeOffset(2026, 2, 11, 12, 0, 0, TimeSpan.FromHours(9));
        await AddRadiruProgramAsync(
            "R1_1",
            "JP13",
            "r1",
            new DateTimeOffset(2026, 2, 11, 23, 30, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 2, 12, 0, 30, 0, TimeSpan.FromHours(9)),
            title: "Midnight1");
        await AddRadiruProgramAsync(
            "R1_2",
            "JP13",
            "r1",
            new DateTimeOffset(2026, 2, 11, 23, 30, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 2, 12, 0, 0, 0, TimeSpan.FromHours(9)),
            title: "Midnight2");
        await AddRadiruProgramAsync(
            "R1_3",
            "JP13",
            "r1",
            new DateTimeOffset(2026, 2, 11, 22, 30, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 2, 11, 23, 30, 0, TimeSpan.FromHours(9)),
            title: "OutsideStart");
        await AddRadiruProgramAsync(
            "R1_4",
            "JP13",
            "r1",
            new DateTimeOffset(2026, 2, 12, 0, 30, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 2, 12, 1, 30, 0, TimeSpan.FromHours(9)),
            title: "OutsideEnd");

        var entity = new ProgramSearchEntity
        {
            SelectedRadiruStationIds = ["JP13:r1"],
            StartTime = new TimeOnly(23, 0),
            EndTime = new TimeOnly(1, 0),
            IncludeHistoricalPrograms = true
        };

        var list = await _repository.SearchRadiruProgramsAsync(entity, now);

        Assert.That(list.Select(x => x.ProgramId), Is.EquivalentTo(new[] { "R1_1", "R1_2" }));
    }

    /// <summary>
    /// らじる★らじる番組検索で全日指定(00:00-23:59)は日跨ぎ番組を含める
    /// </summary>
    [Test]
    public async Task SearchRadiruProgramsAsync_全日指定は日跨ぎ番組を含める()
    {
        var now = new DateTimeOffset(2026, 2, 11, 12, 0, 0, TimeSpan.FromHours(9));
        await AddRadiruProgramAsync(
            "R1_1",
            "JP13",
            "r1",
            new DateTimeOffset(2026, 2, 10, 23, 30, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2026, 2, 11, 0, 30, 0, TimeSpan.FromHours(9)),
            title: "Overnight");

        var entity = new ProgramSearchEntity
        {
            SelectedRadiruStationIds = ["JP13:r1"],
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            IncludeHistoricalPrograms = true
        };

        var list = await _repository.SearchRadiruProgramsAsync(entity, now);

        Assert.That(list.Select(x => x.ProgramId), Is.EquivalentTo(new[] { "R1_1" }));
    }

    /// <summary>
    /// 最終更新日時を保存/取得できる
    /// </summary>
    [Test]
    public async Task SetAndGetLastUpdatedProgramAsync_保存取得()
    {
        var dt = DateTimeOffset.UtcNow;

        await _repository.SetLastUpdatedProgramAsync(dt);

        var stored = await _repository.GetLastUpdatedProgramAsync();
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.Value.UtcDateTime, Is.EqualTo(dt.UtcDateTime));
    }

    /// <summary>
    /// スケジュールジョブ一覧を取得できる
    /// </summary>
    [Test]
    public async Task GetScheduleJobsAsync_取得()
    {
        _dbContext.ScheduleJob.Add(new ScheduleJob
        {
            Id = Ulid.NewUlid(),
            ServiceKind = RadioServiceKind.Radiko,
            StationId = "TBS",
            ProgramId = "P1",
            Title = "Test",
            StartDateTime = DateTime.UtcNow,
            EndDateTime = DateTime.UtcNow.AddMinutes(30),
            RecordingType = RecordingType.RealTime,
            ReserveType = ReserveType.Program,
            IsEnabled = true
        });
        await _dbContext.SaveChangesAsync();

        var list = await _repository.GetScheduleJobsAsync();

        Assert.That(list.Count, Is.EqualTo(1));
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.ChangeTracker.Clear();
    }

    /// <summary>
    /// radiko番組を追加
    /// </summary>
    private async Task<RadikoProgram> AddRadikoProgramAsync(
        string programId,
        string stationId,
        DateTimeOffset start,
        DateTimeOffset end,
        string title = "Title",
        string description = "desc",
        DateOnly? date = null,
        AvailabilityTimeFree availabilityTimeFree = AvailabilityTimeFree.Available)
    {
        var program = CreateRadikoProgram(programId, stationId, start, end, title, description, date, availabilityTimeFree);
        _dbContext.RadikoPrograms.Add(program);
        await _dbContext.SaveChangesAsync();
        return program;
    }

    /// <summary>
    /// radiko番組の作成
    /// </summary>
    private static RadikoProgram CreateRadikoProgram(
        string programId,
        string stationId,
        DateTimeOffset start,
        DateTimeOffset end,
        string title = "Title",
        string description = "desc",
        DateOnly? date = null,
        AvailabilityTimeFree availabilityTimeFree = AvailabilityTimeFree.Available)
    {
        return new RadikoProgram
        {
            ProgramId = programId,
            StationId = stationId,
            Title = title,
            Description = description,
            Performer = "",
            RadioDate = date ?? DateOnly.FromDateTime(start.UtcDateTime),
            DaysOfWeek = DaysOfWeek.Monday,
            StartTime = start.UtcDateTime,
            EndTime = end.UtcDateTime,
            AvailabilityTimeFree = availabilityTimeFree,
            ProgramUrl = ""
        };
    }

    /// <summary>
    /// らじる番組を追加
    /// </summary>
    private async Task AddRadiruProgramAsync(
        string programId,
        string areaId,
        string stationId,
        DateTimeOffset start,
        DateTimeOffset end,
        string title = "Title",
        string description = "desc",
        string? onDemandContentUrl = null,
        DateTime? onDemandExpiresAtUtc = null)
    {
        _dbContext.NhkRadiruPrograms.Add(new NhkRadiruProgram
        {
            ProgramId = programId,
            AreaId = areaId,
            StationId = stationId,
            Title = title,
            Subtitle = "",
            RadioDate = DateOnly.FromDateTime(start.UtcDateTime),
            DaysOfWeek = DaysOfWeek.Monday,
            StartTime = start.UtcDateTime,
            EndTime = end.UtcDateTime,
            Performer = "",
            Description = description,
            EventId = "",
            SiteId = "",
            ProgramUrl = "",
            ImageUrl = "",
            OnDemandContentUrl = onDemandContentUrl,
            OnDemandExpiresAtUtc = onDemandExpiresAtUtc
        });
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// らじる番組の作成
    /// </summary>
    private static NhkRadiruProgram CreateRadiruProgram(
        string programId,
        string areaId,
        string stationId,
        string title)
    {
        return new NhkRadiruProgram
        {
            ProgramId = programId,
            AreaId = areaId,
            StationId = stationId,
            Title = title,
            Subtitle = "",
            RadioDate = DateOnly.FromDateTime(DateTime.UtcNow),
            DaysOfWeek = DaysOfWeek.Monday,
            StartTime = DateTimeOffset.UtcNow.UtcDateTime,
            EndTime = DateTimeOffset.UtcNow.AddMinutes(30).UtcDateTime,
            Performer = "",
            Description = "",
            EventId = "",
            SiteId = "",
            ProgramUrl = "",
            ImageUrl = ""
        };
    }
}
