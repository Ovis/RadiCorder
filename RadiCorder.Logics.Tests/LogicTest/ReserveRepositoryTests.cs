using Microsoft.EntityFrameworkCore;
using RadiCorder.Logics.Infrastructure.Reserve;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Tests.LogicTest;

/// <summary>
/// ReserveRepositoryのテスト
/// </summary>
public class ReserveRepositoryTests : UnitTestBase
{
    private RadioDbContext _dbContext = null!;
    private ReserveRepository _repository = null!;

    [SetUp]
    public async Task Setup()
    {
        _dbContext = DbContext;
        _dbContext.ChangeTracker.Clear();
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM ScheduleJob");
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM KeywordReserveRadioStations");
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM KeywordReserve");
        _repository = new ReserveRepository(_dbContext);
    }

    /// <summary>
    /// 録音予約の追加/取得/更新/削除ができる
    /// </summary>
    [Test]
    public async Task ScheduleJob_CRUD()
    {
        var job = CreateScheduleJob();

        await _repository.AddScheduleJobAsync(job);

        var stored = await _repository.GetScheduleJobByIdAsync(job.Id);
        Assert.That(stored, Is.Not.Null);

        stored!.Title = "Updated";
        await _repository.UpdateScheduleJobAsync(stored);

        var updated = await _repository.GetScheduleJobByIdAsync(job.Id);
        Assert.That(updated!.Title, Is.EqualTo("Updated"));

        await _repository.RemoveScheduleJobAsync(updated);
        var deleted = await _repository.GetScheduleJobByIdAsync(job.Id);
        Assert.That(deleted, Is.Null);
    }

    /// <summary>
    /// マージン更新対象の予約取得
    /// </summary>
    [Test]
    public async Task GetScheduleJobsNeedingDurationUpdateAsync_対象取得()
    {
        var job = CreateScheduleJob();
        job.RecordingType = RecordingType.RealTime;
        job.StartDelay = null;
        job.EndDelay = null;

        _dbContext.ScheduleJob.Add(job);
        await _dbContext.SaveChangesAsync();

        var list = await _repository.GetScheduleJobsNeedingDurationUpdateAsync();

        Assert.That(list.Count, Is.EqualTo(1));
    }

    /// <summary>
    /// 指定日時より古い予約取得
    /// </summary>
    [Test]
    public async Task GetScheduleJobsOlderThanAsync_対象取得()
    {
        var oldJob = CreateScheduleJob();
        oldJob.EndDateTime = DateTime.UtcNow.AddDays(-1);

        _dbContext.ScheduleJob.Add(oldJob);
        await _dbContext.SaveChangesAsync();

        var list = await _repository.GetScheduleJobsOlderThanAsync(DateTimeOffset.UtcNow);

        Assert.That(list.Count, Is.EqualTo(1));
    }

    /// <summary>
    /// キーワード予約の追加/取得/更新/削除ができる
    /// </summary>
    [Test]
    public async Task KeywordReserve_CRUD()
    {
        var reserve = CreateKeywordReserve();
        var stations = new List<KeywordReserveRadioStation>
        {
            new() { Id = reserve.Id, RadioServiceKind = RadioServiceKind.Radiko, RadioStation = "TBS" }
        };

        await _repository.AddKeywordReserveAsync(reserve, stations);

        var stored = await _repository.GetKeywordReserveByIdAsync(reserve.Id);
        Assert.That(stored, Is.Not.Null);

        var newStations = new List<KeywordReserveRadioStation>
        {
            new() { Id = reserve.Id, RadioServiceKind = RadioServiceKind.Radiko, RadioStation = "ABC" }
        };

        stored!.Keyword = "Updated";
        await _repository.UpdateKeywordReserveAsync(stored, newStations);

        var stationsStored = await _repository.GetKeywordReserveRadioStationsAsync();
        Assert.That(stationsStored.Any(s => s.RadioStation == "ABC"), Is.True);

        var deleted = await _repository.DeleteKeywordReserveAsync(reserve.Id);
        Assert.That(deleted, Is.True);

        var deleted2 = await _repository.DeleteKeywordReserveAsync(Ulid.NewUlid());
        Assert.That(deleted2, Is.False);
    }

    /// <summary>
    /// キーワード予約の放送局設定を削除できる
    /// </summary>
    [Test]
    public async Task DeleteKeywordReserveRadioStationsAsync_削除()
    {
        var reserve = CreateKeywordReserve();
        _dbContext.KeywordReserve.Add(reserve);
        _dbContext.KeywordReserveRadioStations.Add(new KeywordReserveRadioStation
        {
            Id = reserve.Id,
            RadioServiceKind = RadioServiceKind.Radiko,
            RadioStation = "TBS"
        });
        await _dbContext.SaveChangesAsync();

        await _repository.DeleteKeywordReserveRadioStationsAsync(reserve.Id);

        var list = await _repository.GetKeywordReserveRadioStationsAsync();
        Assert.That(list.Count, Is.EqualTo(0));
    }

    /// <summary>
    /// キーワード予約IDに紐づく予約を取得できる
    /// </summary>
    [Test]
    public async Task GetScheduleJobsByKeywordReserveIdAsync_取得()
    {
        var reserve = CreateKeywordReserve();
        _dbContext.KeywordReserve.Add(reserve);

        var job = CreateScheduleJob();
        job.KeywordReserveId = reserve.Id;
        _dbContext.ScheduleJob.Add(job);
        await _dbContext.SaveChangesAsync();

        var list = await _repository.GetScheduleJobsByKeywordReserveIdAsync(reserve.Id);

        Assert.That(list.Count, Is.EqualTo(1));
    }

    /// <summary>
    /// 番組IDで予約存在確認できる
    /// </summary>
    [Test]
    public async Task ExistsScheduleJobByProgramIdAsync_存在確認()
    {
        var job = CreateScheduleJob();
        _dbContext.ScheduleJob.Add(job);
        await _dbContext.SaveChangesAsync();

        var exists = await _repository.ExistsScheduleJobByProgramIdAsync(job.ProgramId!);
        var notExists = await _repository.ExistsScheduleJobByProgramIdAsync("notfound");

        Assert.That(exists, Is.True);
        Assert.That(notExists, Is.False);
    }

    /// <summary>
    /// 予約の一括追加/削除ができる
    /// </summary>
    [Test]
    public async Task AddRemoveScheduleJobsAsync_一括処理()
    {
        var jobs = new List<ScheduleJob> { CreateScheduleJob(), CreateScheduleJob() };

        await _repository.AddScheduleJobsAsync(jobs);

        _dbContext.ChangeTracker.Clear();

        var list = await _repository.GetScheduleJobsAsync();
        Assert.That(list.Count, Is.EqualTo(2));

        await _repository.RemoveScheduleJobsAsync(list);

        var list2 = await _repository.GetScheduleJobsAsync();
        Assert.That(list2.Count, Is.EqualTo(0));
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.ChangeTracker.Clear();
    }

    /// <summary>
    /// 録音予約の作成
    /// </summary>
    private static ScheduleJob CreateScheduleJob()
    {
        return new ScheduleJob
        {
            Id = Ulid.NewUlid(),
            ServiceKind = RadioServiceKind.Radiko,
            StationId = "TBS",
            ProgramId = Guid.NewGuid().ToString("N"),
            Title = "Test",
            StartDateTime = DateTime.UtcNow,
            EndDateTime = DateTime.UtcNow.AddMinutes(30),
            RecordingType = RecordingType.RealTime,
            ReserveType = ReserveType.Program,
            IsEnabled = true
        };
    }

    /// <summary>
    /// キーワード予約の作成
    /// </summary>
    private static KeywordReserve CreateKeywordReserve()
    {
        return new KeywordReserve
        {
            Id = Ulid.NewUlid(),
            Keyword = "radio",
            ExcludedKeyword = "",
            FileName = "",
            FolderPath = "",
            IsEnable = true,
            IsTitleOnly = true,
            IsExcludeTitleOnly = false,
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            DaysOfWeek = DaysOfWeek.Monday
        };
    }
}
