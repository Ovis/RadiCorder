using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Infrastructure.Recording;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Tests.LogicTest;

/// <summary>
/// RecordingRepositoryのテスト
/// </summary>
public class RecordingRepositoryTests : UnitTestBase
{
    private RadioDbContext _dbContext = null!;
    private RecordingRepository _repository = null!;

    [SetUp]
    public async Task Setup()
    {
        _dbContext = DbContext;
        _dbContext.ChangeTracker.Clear();
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM RecordingFiles");
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM RecordingMetadatas");
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM Recordings");
        _repository = new RecordingRepository(new Mock<ILogger<RecordingRepository>>().Object, _dbContext);
    }

    /// <summary>
    /// 録音レコードが作成される
    /// </summary>
    [Test]
    public async Task CreateAsync_レコード作成()
    {
        var program = CreateProgramInfo();
        var path = new MediaPath("temp.m4a", "final.m4a", "rel\\final.m4a");
        var options = new RecordingOptions(Models.Enums.RadioServiceKind.Radiko, false, 0, 0);

        var id = await _repository.CreateAsync(program, path, options);

        var recording = await _dbContext.Recordings.FindAsync(id);
        var metadata = await _dbContext.RecordingMetadatas.FindAsync(id);
        var file = await _dbContext.RecordingFiles.FindAsync(id);

        Assert.That(recording, Is.Not.Null);
        Assert.That(recording!.State, Is.EqualTo(RecordingState.Pending));
        Assert.That(metadata, Is.Not.Null);
        Assert.That(file, Is.Not.Null);
        Assert.That(file!.FileRelativePath, Is.EqualTo("rel\\final.m4a"));
    }

    /// <summary>
    /// 録音状態の更新ができる
    /// </summary>
    [Test]
    public async Task UpdateStateAsync_更新できる()
    {
        var program = CreateProgramInfo();
        var path = new MediaPath("temp.m4a", "final.m4a", "rel\\final.m4a");
        var options = new RecordingOptions(Models.Enums.RadioServiceKind.Radiko, false, 0, 0);

        var id = await _repository.CreateAsync(program, path, options);

        await _repository.UpdateStateAsync(id, RecordingState.Completed, "ok");

        var updated = await _dbContext.Recordings.FindAsync(id);
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.State, Is.EqualTo(RecordingState.Completed));
        Assert.That(updated.ErrorMessage, Is.EqualTo("ok"));
    }

    /// <summary>
    /// 対象がない場合は例外にならない
    /// </summary>
    [Test]
    public async Task UpdateStateAsync_対象なし_例外なし()
    {
        Assert.DoesNotThrowAsync(async () =>
            await _repository.UpdateStateAsync(Ulid.NewUlid(), RecordingState.Completed));
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.ChangeTracker.Clear();
    }

    /// <summary>
    /// テスト用番組情報
    /// </summary>
    private static ProgramRecordingInfo CreateProgramInfo()
        => new(
            ProgramId: "P1",
            Title: "TestTitle",
            Subtitle: "Sub",
            StationId: "TBS",
            StationName: "TBS",
            AreaId: "JP13",
            StartTime: DateTimeOffset.UtcNow,
            EndTime: DateTimeOffset.UtcNow.AddMinutes(30),
            Performer: "Tester",
            Description: "Desc",
            ProgramUrl: "http://example");
}
