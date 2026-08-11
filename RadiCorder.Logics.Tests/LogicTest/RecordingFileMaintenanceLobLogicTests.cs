using Microsoft.Extensions.Logging;
using Moq;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Logics.RecordedRadioLogic;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Tests.LogicTest;

/// <summary>
/// RecordingFileMaintenanceLobLogic のテスト
/// </summary>
public class RecordingFileMaintenanceLobLogicTests : UnitTestBase
{
    private string _rootPath = string.Empty;
    private RecordingFileMaintenanceLobLogic _logic = null!;

    [SetUp]
    public void Setup()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"radicorder-maintenance-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootPath);

        var configMock = new Mock<IAppConfigurationService>();
        configMock.SetupGet(x => x.RecordFileSaveDir).Returns(_rootPath);

        _logic = new RecordingFileMaintenanceLobLogic(
            new Mock<ILogger<RecordingFileMaintenanceLobLogic>>().Object,
            configMock.Object,
            DbContext);
    }

    [TearDown]
    public async Task TearDown()
    {
        DbContext.RecordingFiles.RemoveRange(DbContext.RecordingFiles);
        DbContext.RecordingMetadatas.RemoveRange(DbContext.RecordingMetadatas);
        DbContext.Recordings.RemoveRange(DbContext.Recordings);
        await DbContext.SaveChangesAsync();

        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }

    [Test]
    public async Task ScanMissingRecordsAsync_欠損レコードを抽出できる()
    {
        var existingPath = Path.Combine(_rootPath, "exists.mp3");
        await File.WriteAllBytesAsync(existingPath, [0x00]);
        await AddRecordingAsync(existingPath, "Exists");

        var missingStoredPath = Path.Combine("lost", "target.mp3");
        await AddRecordingAsync(Path.Combine(_rootPath, missingStoredPath), "Missing", false);

        var candidatePath = Path.Combine(_rootPath, "candidate", "target.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(candidatePath)!);
        await File.WriteAllBytesAsync(candidatePath, [0x01]);

        var result = await _logic.ScanMissingRecordsAsync();

        Assert.That(result.MissingCount, Is.EqualTo(1));
        Assert.That(result.Entries.Count, Is.EqualTo(1));
        Assert.That(result.Entries[0].Title, Is.EqualTo("Missing"));
        Assert.That(result.Entries[0].CandidateCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RelinkMissingRecordsAsync_候補が一件なら再紐付けできる()
    {
        var missingPath = Path.Combine("old", "program.mp3");
        var recordingId = await AddRecordingAsync(Path.Combine(_rootPath, missingPath), "RelinkTarget", false);

        var candidatePath = Path.Combine(_rootPath, "new", "program.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(candidatePath)!);
        await File.WriteAllBytesAsync(candidatePath, [0x11, 0x22]);

        var result = await _logic.RelinkMissingRecordsAsync([recordingId]);

        Assert.That(result.TargetCount, Is.EqualTo(1));
        Assert.That(result.SuccessCount, Is.EqualTo(1));
        Assert.That(result.FailCount, Is.EqualTo(0));

        var file = await DbContext.RecordingFiles.FindAsync(recordingId);
        Assert.That(file, Is.Not.Null);
        Assert.That(file!.FileRelativePath, Is.EqualTo(Path.GetRelativePath(_rootPath, candidatePath)));
    }

    [Test]
    public async Task ScanMissingRecordsAsync_他レコード参照中の候補は除外される()
    {
        var inUsePath = Path.Combine(_rootPath, "in-use", "shared.mp3");
        await AddRecordingAsync(inUsePath, "InUseRecord", true);

        var missingStoredPath = Path.Combine("missing", "shared.mp3");
        await AddRecordingAsync(Path.Combine(_rootPath, missingStoredPath), "MissingRecord", false);

        var result = await _logic.ScanMissingRecordsAsync();

        Assert.That(result.MissingCount, Is.EqualTo(1));
        Assert.That(result.Entries.Count(x => x.IssueType == "missing_file"), Is.EqualTo(1));
        var missingEntry = result.Entries.Single(x => x.IssueType == "missing_file");
        Assert.That(missingEntry.CandidateCount, Is.EqualTo(0));
        Assert.That(missingEntry.CandidateRelativePaths, Is.Empty);
    }

    [Test]
    public async Task DeleteMissingRecordsAsync_欠損レコードをDBから削除できる()
    {
        var missingPath = Path.Combine("missing", "delete-target.mp3");
        var recordingId = await AddRecordingAsync(Path.Combine(_rootPath, missingPath), "DeleteTarget", false);

        var result = await _logic.DeleteMissingRecordsAsync([recordingId]);

        Assert.That(result.TargetCount, Is.EqualTo(1));
        Assert.That(result.SuccessCount, Is.EqualTo(1));
        Assert.That(await DbContext.Recordings.FindAsync(recordingId), Is.Null);
    }

    [Test]
    public async Task ScanMissingRecordsAsync_Failedかつファイル存在レコードを復旧候補として抽出できる()
    {
        var failedPath = Path.Combine(_rootPath, "recover", "failed-target.mp3");
        var failedId = await AddRecordingAsync(failedPath, "RecoverTarget", true, RecordingState.Failed, "保存エラー");

        var result = await _logic.ScanMissingRecordsAsync();

        Assert.That(result.RecoverableFailedCount, Is.EqualTo(1));
        var target = result.Entries.SingleOrDefault(x => x.RecordingId == failedId.ToString());
        Assert.That(target, Is.Not.Null);
        Assert.That(target!.IssueType, Is.EqualTo("failed_with_existing_file"));
    }

    [Test]
    public async Task RelinkMissingRecordsAsync_Failedかつファイル存在レコードをCompletedへ復旧できる()
    {
        var failedPath = Path.Combine(_rootPath, "recover", "failed-to-completed.mp3");
        var failedId = await AddRecordingAsync(failedPath, "RecoverActionTarget", true, RecordingState.Failed, "保存エラー");

        var result = await _logic.RelinkMissingRecordsAsync([failedId]);

        Assert.That(result.TargetCount, Is.EqualTo(1));
        Assert.That(result.SuccessCount, Is.EqualTo(1));
        Assert.That(result.FailCount, Is.EqualTo(0));

        var recording = await DbContext.Recordings.FindAsync(failedId);
        Assert.That(recording, Is.Not.Null);
        Assert.That(recording!.State, Is.EqualTo(RecordingState.Completed));
        Assert.That(recording.ErrorMessage, Is.Null);
    }

    private async ValueTask<Ulid> AddRecordingAsync(
        string fullPath,
        string title,
        bool createFile = true,
        RecordingState state = RecordingState.Completed,
        string? errorMessage = null)
    {
        if (createFile)
        {
            var dir = Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(fullPath, [0x01, 0x02]);
        }

        var id = Ulid.NewUlid();
        await DbContext.Recordings.AddAsync(new Recording
        {
            Id = id,
            ServiceKind = RadioServiceKind.Radiko,
            ProgramId = $"P-{id}",
            StationId = "TBS",
            AreaId = "JP13",
            StartDateTime = DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime,
            EndDateTime = DateTimeOffset.UtcNow.UtcDateTime,
            IsTimeFree = false,
            State = state,
            ErrorMessage = errorMessage,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            SourceType = RecordingSourceType.Recorded
        });

        await DbContext.RecordingMetadatas.AddAsync(new RecordingMetadata
        {
            RecordingId = id,
            StationName = "TBS",
            Title = title,
            Subtitle = string.Empty,
            Performer = string.Empty,
            Description = string.Empty,
            ProgramUrl = string.Empty
        });

        await DbContext.RecordingFiles.AddAsync(new RecordingFile
        {
            RecordingId = id,
            FileRelativePath = Path.GetRelativePath(_rootPath, fullPath),
            HasHlsFile = false
        });

        await DbContext.SaveChangesAsync();
        return id;
    }
}
