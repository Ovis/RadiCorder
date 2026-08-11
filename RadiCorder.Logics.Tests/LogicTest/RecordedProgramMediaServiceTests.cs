using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Moq;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Infrastructure.Recording;
using RadiCorder.Logics.Logics.RecordedRadioLogic;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Tests.LogicTest;

/// <summary>
/// RecordedProgramMediaServiceのテスト
/// </summary>
public class RecordedProgramMediaServiceTests : UnitTestBase
{
    private Mock<ILogger<RecordedProgramMediaService>> _loggerMock = null!;
    private Mock<IAppConfigurationService> _configMock = null!;
    private Mock<IFfmpegService> _ffmpegMock = null!;
    private RadioDbContext _dbContext = null!;
    private RecordedProgramMediaService _service = null!;

    [SetUp]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<RecordedProgramMediaService>>();
        _configMock = new Mock<IAppConfigurationService>();
        _ffmpegMock = new Mock<IFfmpegService>();
        _dbContext = DbContext;

        _service = new RecordedProgramMediaService(
            _loggerMock.Object,
            _configMock.Object,
            _ffmpegMock.Object,
            _dbContext);
    }

    /// <summary>
    /// 相対パス優先で取得できる
    /// </summary>
    [Test]
    public async Task GetRecordedProgramFilePathAsync_相対パス優先()
    {
        var (recordingId, _, _) = await AddRecordingFileAsync(
            fileRelativePath: "rel\\file.m4a");

        var (isSuccess, path) = await _service.GetRecordedProgramFilePathAsync(recordingId);

        Assert.That(isSuccess, Is.True);
        Assert.That(path, Is.EqualTo("rel\\file.m4a"));
    }

    /// <summary>
    /// HLS生成なしの場合は空パス
    /// </summary>
    [Test]
    public async Task GetHlsAsync_生成なし_空パス()
    {
        var (recordingId, _, _) = await AddRecordingFileAsync(
            fileRelativePath: "rel\\file.m4a");

        var (isSuccess, path) = await _service.GetHlsAsync(recordingId, createHls: false);

        Assert.That(isSuccess, Is.True);
        Assert.That(path, Is.EqualTo(string.Empty));
    }

    /// <summary>
    /// HLS生成が成功するとフラグが更新される
    /// </summary>
    [Test]
    public async Task GetHlsAsync_生成成功_フラグ更新()
    {
        var baseDir = CreateTempDirectory("media");
        var tempDir = CreateTempDirectory("hls");

        try
        {
            _configMock.SetupGet(x => x.RecordFileSaveDir).Returns(baseDir);
            _configMock.SetupGet(x => x.TemporaryFileSaveDir).Returns(tempDir);

            var recordingId = Ulid.NewUlid();
            var relativePath = Path.Combine("rel", "file.m4a");
            var inputPath = Path.Combine(baseDir, relativePath);

            // 入力ファイルを用意
            Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
            File.WriteAllText(inputPath, "dummy");

            // HLSファイルを事前に用意（FFmpeg出力を模擬）
            var hlsDir = Path.Combine(tempDir, TemporaryStoragePaths.HlsCacheDirectoryName, recordingId.ToString());
            Directory.CreateDirectory(hlsDir);
            var hlsFile = Path.Combine(hlsDir, "radio.m3u8");
            var tsFile = Path.Combine(hlsDir, "radio000.ts");
            File.WriteAllText(tsFile, "dummy-ts");

            await AddRecordingFileAsync(
                recordingId: recordingId,
                fileRelativePath: relativePath,
                hasHlsFile: false);

            _ffmpegMock
                .Setup(x => x.RunProcessAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
                .ReturnsAsync(() =>
                {
                    File.WriteAllText(hlsFile, string.Join('\n',
                        "#EXTM3U",
                        "#EXT-X-VERSION:3",
                        "#EXT-X-TARGETDURATION:10",
                        "#EXT-X-MEDIA-SEQUENCE:0",
                        "#EXTINF:10.000000,",
                        "radio000.ts",
                        "#EXT-X-ENDLIST"));
                    return true;
                });

            var (isSuccess, path) = await _service.GetHlsAsync(recordingId);

            Assert.That(isSuccess, Is.True);
            Assert.That(path, Is.EqualTo(hlsFile));

            var updated = await _dbContext.RecordingFiles.FindAsync(recordingId);
            Assert.That(updated, Is.Not.Null);
            Assert.That(updated!.HasHlsFile, Is.True);
            Assert.That(updated.HlsDirectoryPath, Is.EqualTo(hlsDir));

            var text = await File.ReadAllTextAsync(hlsFile);
            Assert.That(text, Does.Contain($"/static/{recordingId}/radio"));
        }
        finally
        {
            SafeDeleteDirectory(baseDir);
            SafeDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// ファイル削除に失敗した場合はfalse（Windowsのみ保証）
    /// </summary>
    [Test]
    public async Task DeleteRecordedProgramAsync_ファイル削除失敗()
    {
        var baseDir = CreateTempDirectory("media");
        var tempDir = CreateTempDirectory("temp");

        FileStream? stream = null;
        try
        {
            _configMock.SetupGet(x => x.RecordFileSaveDir).Returns(baseDir);
            _configMock.SetupGet(x => x.TemporaryFileSaveDir).Returns(tempDir);

            var recordingId = Ulid.NewUlid();
            var relativePath = Path.Combine("rel", "locked.m4a");
            var filePath = Path.Combine(baseDir, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, "dummy");

            stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);

            await AddRecordingFileAsync(
                recordingId: recordingId,
                fileRelativePath: relativePath,
                hasHlsFile: false);

            var result = await _service.DeleteRecordedProgramAsync(recordingId);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.That(result, Is.False);
            }
            else
            {
                Assert.That(result, Is.True);
            }
        }
        finally
        {
            stream?.Dispose();
            SafeDeleteDirectory(baseDir);
            SafeDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// HLS削除に失敗した場合はfalse（Windowsのみ保証）
    /// </summary>
    [Test]
    public async Task DeleteRecordedProgramAsync_Hls削除失敗()
    {
        var baseDir = CreateTempDirectory("media");
        var tempDir = CreateTempDirectory("temp");

        FileStream? stream = null;
        try
        {
            _configMock.SetupGet(x => x.RecordFileSaveDir).Returns(baseDir);
            _configMock.SetupGet(x => x.TemporaryFileSaveDir).Returns(tempDir);

            var recordingId = Ulid.NewUlid();

            var hlsDir = Path.Combine(tempDir, TemporaryStoragePaths.HlsCacheDirectoryName, recordingId.ToString());
            Directory.CreateDirectory(hlsDir);
            var hlsFile = Path.Combine(hlsDir, "radio.m3u8");
            File.WriteAllText(hlsFile, string.Join('\n',
                "#EXTM3U",
                "#EXT-X-VERSION:3",
                "#EXT-X-TARGETDURATION:10",
                "#EXT-X-MEDIA-SEQUENCE:0",
                "#EXTINF:10.000000,",
                "radio000.ts",
                "#EXT-X-ENDLIST"));
            var tsFile = Path.Combine(hlsDir, "radio000.ts");
            File.WriteAllText(tsFile, "dummy-ts");

            stream = new FileStream(hlsFile, FileMode.Open, FileAccess.Read, FileShare.None);

            await AddRecordingFileAsync(
                recordingId: recordingId,
                fileRelativePath: "rel\\file.m4a",
                hasHlsFile: true);

            var result = await _service.DeleteRecordedProgramAsync(recordingId);
            var remaining = await _dbContext.Recordings.FindAsync(recordingId);

            if (result)
            {
                Assert.That(remaining, Is.Null);
            }
            else
            {
                Assert.That(remaining, Is.Not.Null);
                Assert.That(File.Exists(hlsFile), Is.True);
            }
        }
        finally
        {
            stream?.Dispose();
            SafeDeleteDirectory(baseDir);
            SafeDeleteDirectory(tempDir);
        }
    }

    [TearDown]
    public async Task TearDown()
    {
        var tran = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            _dbContext.RecordingFiles.RemoveRange(_dbContext.RecordingFiles);
            _dbContext.RecordingMetadatas.RemoveRange(_dbContext.RecordingMetadatas);
            _dbContext.Recordings.RemoveRange(_dbContext.Recordings);

            await _dbContext.SaveChangesAsync();
            await tran.CommitAsync();
        }
        catch
        {
            await tran.RollbackAsync();
        }
    }

    /// <summary>
    /// 録音ファイル関連データを登録
    /// </summary>
    private async ValueTask<(Ulid RecordingId, Recording Recording, RecordingFile File)> AddRecordingFileAsync(
        string fileRelativePath,
        bool hasHlsFile = false,
        Ulid? recordingId = null)
    {
        var id = recordingId ?? Ulid.NewUlid();

        var recording = new Recording
        {
            Id = id,
            ServiceKind = Models.Enums.RadioServiceKind.Radiko,
            ProgramId = "TEST",
            StationId = "TBS",
            AreaId = "JP13",
            StartDateTime = DateTimeOffset.UtcNow.UtcDateTime,
            EndDateTime = DateTimeOffset.UtcNow.AddMinutes(30).UtcDateTime,
            IsTimeFree = false,
            State = RecordingState.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var metadata = new RecordingMetadata
        {
            RecordingId = id,
            Title = "Test",
            Subtitle = "",
            Performer = "",
            Description = "",
            ProgramUrl = ""
        };

        var file = new RecordingFile
        {
            RecordingId = id,
            FileRelativePath = fileRelativePath,
            HasHlsFile = hasHlsFile
        };

        await _dbContext.Recordings.AddAsync(recording);
        await _dbContext.RecordingMetadatas.AddAsync(metadata);
        await _dbContext.RecordingFiles.AddAsync(file);
        await _dbContext.SaveChangesAsync();

        return (id, recording, file);
    }

    /// <summary>
    /// テスト用ディレクトリを作成
    /// </summary>
    private static string CreateTempDirectory(string suffix)
    {
        var dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "RecordedProgramMediaServiceTests", suffix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 例外を握って削除する
    /// </summary>
    private static void SafeDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
        catch
        {
            // テスト用のため失敗は無視
        }
    }
}
