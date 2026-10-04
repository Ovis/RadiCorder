using Moq;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Infrastructure.Recording;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Tests.LogicTest;

/// <summary>
/// MediaStorageServiceのテスト
/// </summary>
public class MediaStorageServiceTests
{
    [Test]
    public void 他の復旧情報が破損していても対象ジョブの後処理を完了する()
    {
        var root = CreateTempDirectory();
        try
        {
            var config = CreateConfig(root, root, null, null);
            var journal = new RecordingFinalizationJournal(config.Object);
            var jobId = Ulid.NewUlid();
            var targetId = Ulid.NewUlid();
            var otherId = Ulid.NewUlid();
            journal.Write(new(targetId, jobId.ToString(), new MediaPath("temp", Path.Combine(root, "good.m4a"), "good.m4a")));
            journal.Write(new(otherId, Ulid.NewUlid().ToString(), new MediaPath("temp", Path.Combine(root, "other.m4a"), "other.m4a")));
            var broken = Path.Combine(root, "recording-finalization", Ulid.NewUlid() + ".json");
            File.WriteAllText(broken, "{broken");
            new MediaStorageService(config.Object, finalizationJournal: journal).CompleteJobFinalization(jobId);
            Assert.That(journal.GetPendingFiles().Select(Path.GetFileName), Is.EquivalentTo(new[] { Path.GetFileName(broken), otherId + ".json" }));
        }
        finally { SafeDeleteDirectory(root); }
    }

    /// <summary>
    /// テンプレート指定時にパスが正しく構成される
    /// </summary>
    [Test]
    public async Task PrepareAsync_TemplateAndRelativePath_正しく構成される()
    {
        var baseDir = CreateTempDirectory();
        var tempDir = CreateTempDirectory();

        try
        {
            var configMock = CreateConfig(
                baseDir,
                tempDir,
                recordDirectoryRelativePath: "$StationId$\\$SYYYY$",
                recordFileNameTemplate: "$Title$");

            var service = new MediaStorageService(configMock.Object);
            var program = CreateProgramInfo();

            var result = await service.PrepareAsync(program, CreateOptions());

            var expectedRelative = Path.Combine("TBS", "2026", "TestTitle.m4a");
            var expectedFull = Path.Combine(baseDir, expectedRelative);

            Assert.That(result.RelativePath, Is.EqualTo(expectedRelative));
            Assert.That(result.FinalFilePath, Is.EqualTo(expectedFull));
            Assert.That(Path.GetDirectoryName(result.TempFilePath), Is.EqualTo(Path.Combine(tempDir, TemporaryStoragePaths.RecordingsWorkDirectoryName)));
            Assert.That(Path.GetFileName(result.TempFilePath), Does.StartWith("P1_"));
            Assert.That(Path.GetExtension(result.TempFilePath), Is.EqualTo(".m4a"));
        }
        finally
        {
            SafeDeleteDirectory(baseDir);
            SafeDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// 同一番組でも準備時の一時ファイル名は重複しない
    /// </summary>
    [Test]
    public async Task PrepareAsync_同一番組を連続実行_一時ファイルが重複しない()
    {
        var baseDir = CreateTempDirectory();
        var tempDir = CreateTempDirectory();

        try
        {
            var configMock = CreateConfig(baseDir, tempDir, null, "$Title$");
            var service = new MediaStorageService(configMock.Object);
            var program = CreateProgramInfo();

            var first = await service.PrepareAsync(program, CreateOptions());
            var second = await service.PrepareAsync(program, CreateOptions());

            Assert.That(first.TempFilePath, Is.Not.EqualTo(second.TempFilePath));
            Assert.That(Path.GetFileName(first.TempFilePath), Does.StartWith("P1_"));
            Assert.That(Path.GetFileName(second.TempFilePath), Does.StartWith("P1_"));
        }
        finally
        {
            SafeDeleteDirectory(baseDir);
            SafeDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// 保存先が不正な場合は例外
    /// </summary>
    [Test]
    public void PrepareAsync_保存先不正_例外()
    {
        var configMock = CreateConfig(
            recordFileSaveDir: "relative",
            temporaryFileSaveDir: "temp",
            recordDirectoryRelativePath: "folder",
            recordFileNameTemplate: "$Title$");

        var service = new MediaStorageService(configMock.Object);
        var program = CreateProgramInfo();

        Assert.ThrowsAsync<DomainException>(async () => await service.PrepareAsync(program, CreateOptions()));
    }

    /// <summary>
    /// 既存ファイルがある場合はリネーム保存する
    /// </summary>
    [Test]
    public async Task CommitAsync_既存ファイルあり_リネーム保存()
    {
        var baseDir = CreateTempDirectory();
        var tempDir = CreateTempDirectory();

        try
        {
            var configMock = CreateConfig(
                baseDir,
                tempDir,
                recordDirectoryRelativePath: null,
                recordFileNameTemplate: "TestTitle");

            var service = new MediaStorageService(configMock.Object);
            var program = CreateProgramInfo();
            var path = await service.PrepareAsync(program, CreateOptions());

            // 既存ファイルを作成
            Directory.CreateDirectory(Path.GetDirectoryName(path.FinalFilePath)!);
            File.WriteAllText(path.FinalFilePath, "old");

            // 一時ファイルを作成
            File.WriteAllText(path.TempFilePath, "new");

            await service.CommitAsync(path);

            Assert.That(File.Exists(path.TempFilePath), Is.False);
            Assert.That(File.Exists(path.FinalFilePath), Is.True);

            var files = Directory.GetFiles(Path.GetDirectoryName(path.FinalFilePath)!, "TestTitle_duplicate_*.m4a");
            Assert.That(files.Length, Is.EqualTo(1));
        }
        finally
        {
            SafeDeleteDirectory(baseDir);
            SafeDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// 一時ファイルの削除
    /// </summary>
    [Test]
    public async Task CleanupTempAsync_一時ファイル削除()
    {
        var baseDir = CreateTempDirectory();
        var tempDir = CreateTempDirectory();

        try
        {
            var configMock = CreateConfig(baseDir, tempDir, null, null);
            var service = new MediaStorageService(configMock.Object);
            var program = CreateProgramInfo();
            var path = await service.PrepareAsync(program, CreateOptions());

            File.WriteAllText(path.TempFilePath, "temp");

            await service.CleanupTempAsync(path);

            Assert.That(File.Exists(path.TempFilePath), Is.False);
        }
        finally
        {
            SafeDeleteDirectory(baseDir);
            SafeDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// 最終保存失敗時に一時保存領域へ退避し、メタ情報を出力する
    /// </summary>
    [Test]
    public async Task SaveFailedAsync_退避保存とメタ情報出力()
    {
        var baseDir = CreateTempDirectory();
        var tempDir = CreateTempDirectory();

        try
        {
            var configMock = CreateConfig(baseDir, tempDir, null, null);
            var service = new MediaStorageService(configMock.Object);
            var program = CreateProgramInfo();
            var path = await service.PrepareAsync(program, CreateOptions());

            File.WriteAllText(path.TempFilePath, "temp-recorded");

            var result = await service.SaveFailedAsync(
                path,
                new SaveFailedFallbackMetadata(
                    RecordedAt: DateTimeOffset.UtcNow,
                    ProgramId: "P1",
                    StationId: "TBS",
                    Title: "TestTitle",
                    OriginalDestinationPath: path.FinalFilePath,
                    ErrorType: "UnauthorizedAccessException",
                    ErrorMessage: "Access denied",
                    ExpectedTags: new Dictionary<string, string>
                    {
                        ["title"] = "TestTitle",
                        ["artist"] = "Tester",
                        ["comment"] = "Desc",
                        ["date"] = "2026-02-09T03:00:00.0000000+00:00"
                    }));

            var saveFailedRoot = Path.Combine(tempDir, TemporaryStoragePaths.SaveFailedDirectoryName);
            Assert.That(result.FilePath, Does.StartWith(saveFailedRoot));
            Assert.That(File.Exists(result.FilePath), Is.True);
            Assert.That(File.Exists(path.TempFilePath), Is.False);
            Assert.That(result.MetadataPath, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(result.MetadataPath!), Is.True);
        }
        finally
        {
            SafeDeleteDirectory(baseDir);
            SafeDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// 全トークンが期待どおりに展開される
    /// </summary>
    [Test]
    public async Task PrepareAsync_全トークンを含むテンプレート_正しく展開される()
    {
        var baseDir = CreateTempDirectory();
        var tempDir = CreateTempDirectory();

        try
        {
            var configMock = CreateConfig(
                baseDir,
                tempDir,
                recordDirectoryRelativePath: "$StationName$\\$EYYYY$\\$EMM$\\$EDD$",
                recordFileNameTemplate:
                "$StationId$_$Title$_$SYYYY$_$SYY$_$SMM$_$SM$_$SDD$_$SD$_$STHH$_$STH$_$STMM$_$STM$_$STSS$_$STS$_$EYYYY$_$EYY$_$EMM$_$EM$_$EDD$_$ED$_$ETHH$_$ETH$_$ETMM$_$ETM$_$ETSS$_$ETS$");

            var service = new MediaStorageService(configMock.Object);
            var program = new ProgramRecordingInfo(
                ProgramId: "P2",
                Title: "Title:/?A",
                Subtitle: "Sub",
                StationId: "ST:01",
                StationName: "Station/Name",
                AreaId: "JP13",
                StartTime: new DateTimeOffset(2026, 2, 9, 12, 3, 4, TimeSpan.FromHours(9)),
                EndTime: new DateTimeOffset(2026, 12, 31, 23, 45, 56, TimeSpan.FromHours(9)),
                Performer: "Tester",
                Description: "Desc",
                ProgramUrl: "http://example");

            var result = await service.PrepareAsync(program, CreateOptions());
            var expectedFileName =
                "ST：01_Title：／？A_2026_26_02_2_09_9_12_12_03_3_04_4_2026_26_12_12_31_31_23_23_45_45_56_56.m4a";
            var expectedRelative = Path.Combine("Station／Name", "2026", "12", "31", expectedFileName);

            Assert.That(result.RelativePath, Is.EqualTo(expectedRelative));
            Assert.That(Path.GetFileName(result.FinalFilePath), Is.EqualTo(expectedFileName));
        }
        finally
        {
            SafeDeleteDirectory(baseDir);
            SafeDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// ファイル名テンプレートが不正な場合は既定ファイル名へフォールバックする
    /// </summary>
    [Test]
    public async Task PrepareAsync_不正なファイル名テンプレート_既定ファイル名にフォールバックする()
    {
        var baseDir = CreateTempDirectory();
        var tempDir = CreateTempDirectory();

        try
        {
            var configMock = CreateConfig(
                baseDir,
                tempDir,
                recordDirectoryRelativePath: "$StationId$",
                recordFileNameTemplate: "$Title$/$SYYYY$");

            var service = new MediaStorageService(configMock.Object);
            var program = CreateProgramInfo();

            var result = await service.PrepareAsync(program, CreateOptions());
            var expectedFileName = "20260209120000_TestTitle.m4a";

            Assert.That(Path.GetFileName(result.FinalFilePath), Is.EqualTo(expectedFileName));
            Assert.That(result.RelativePath, Is.EqualTo(Path.Combine("TBS", expectedFileName)));
        }
        finally
        {
            SafeDeleteDirectory(baseDir);
            SafeDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// 保存先相対パステンプレートが不正な場合は保存先ルートにフォールバックする
    /// </summary>
    [Test]
    public async Task PrepareAsync_不正な相対パステンプレート_保存先ルートにフォールバックする()
    {
        var baseDir = CreateTempDirectory();
        var tempDir = CreateTempDirectory();

        try
        {
            var configMock = CreateConfig(
                baseDir,
                tempDir,
                recordDirectoryRelativePath: "..\\Escape",
                recordFileNameTemplate: "$Title$");

            var service = new MediaStorageService(configMock.Object);
            var program = CreateProgramInfo();

            var result = await service.PrepareAsync(program, CreateOptions());

            Assert.That(result.RelativePath, Is.EqualTo("TestTitle.m4a"));
            Assert.That(result.FinalFilePath, Is.EqualTo(Path.Combine(baseDir, "TestTitle.m4a")));
        }
        finally
        {
            SafeDeleteDirectory(baseDir);
            SafeDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// ルート付きテンプレートは相対化して保存先配下として解釈する
    /// </summary>
    [Test]
    public async Task PrepareAsync_ルート付き相対パステンプレート_ルートを除去して適用する()
    {
        var baseDir = CreateTempDirectory();
        var tempDir = CreateTempDirectory();

        try
        {
            var rootedTemplate = Path.Combine(Path.GetPathRoot(baseDir)!, "Music", "$StationId$");
            var configMock = CreateConfig(
                baseDir,
                tempDir,
                recordDirectoryRelativePath: rootedTemplate,
                recordFileNameTemplate: "$Title$");

            var service = new MediaStorageService(configMock.Object);
            var program = CreateProgramInfo();

            var result = await service.PrepareAsync(program, CreateOptions());
            var expectedRelative = Path.Combine("Music", "TBS", "TestTitle.m4a");

            Assert.That(result.RelativePath, Is.EqualTo(expectedRelative));
            Assert.That(result.FinalFilePath, Is.EqualTo(Path.Combine(baseDir, expectedRelative)));
        }
        finally
        {
            SafeDeleteDirectory(baseDir);
            SafeDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// ルール側の保存先・ファイル名がある場合はアプリ全体設定より優先する
    /// </summary>
    [Test]
    public async Task PrepareAsync_ルールオーバーライドあり_ルール値を優先する()
    {
        var baseDir = CreateTempDirectory();
        var tempDir = CreateTempDirectory();

        try
        {
            var configMock = CreateConfig(
                baseDir,
                tempDir,
                recordDirectoryRelativePath: "global\\$StationId$",
                recordFileNameTemplate: "global_$Title$");

            var service = new MediaStorageService(configMock.Object);
            var program = CreateProgramInfo();

            var result = await service.PrepareAsync(
                program,
                CreateOptions(
                    outputDirectoryRelativePathOverride: "rule\\$StationId$",
                    outputFileNameTemplateOverride: "rule_$Title$"));

            var expectedRelative = Path.Combine("rule", "TBS", "rule_TestTitle.m4a");

            Assert.That(result.RelativePath, Is.EqualTo(expectedRelative));
            Assert.That(result.FinalFilePath, Is.EqualTo(Path.Combine(baseDir, expectedRelative)));
        }
        finally
        {
            SafeDeleteDirectory(baseDir);
            SafeDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// ルール側の保存先・ファイル名が不正な場合はアプリ全体設定へフォールバックする
    /// </summary>
    [Test]
    public async Task PrepareAsync_不正なルールオーバーライド_アプリ設定へフォールバックする()
    {
        var baseDir = CreateTempDirectory();
        var tempDir = CreateTempDirectory();

        try
        {
            var configMock = CreateConfig(
                baseDir,
                tempDir,
                recordDirectoryRelativePath: "global\\$StationId$",
                recordFileNameTemplate: "global_$Title$");

            var service = new MediaStorageService(configMock.Object);
            var program = CreateProgramInfo();

            var result = await service.PrepareAsync(
                program,
                CreateOptions(
                    outputDirectoryRelativePathOverride: "..\\escape",
                    outputFileNameTemplateOverride: "bad/$Title$"));

            var expectedRelative = Path.Combine("global", "TBS", "global_TestTitle.m4a");

            Assert.That(result.RelativePath, Is.EqualTo(expectedRelative));
            Assert.That(result.FinalFilePath, Is.EqualTo(Path.Combine(baseDir, expectedRelative)));
        }
        finally
        {
            SafeDeleteDirectory(baseDir);
            SafeDeleteDirectory(tempDir);
        }
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
            StartTime: new DateTimeOffset(2026, 2, 9, 12, 0, 0, TimeSpan.FromHours(9)),
            EndTime: new DateTimeOffset(2026, 2, 9, 12, 30, 0, TimeSpan.FromHours(9)),
            Performer: "Tester",
            Description: "Desc",
            ProgramUrl: "http://example");

    private static RecordingOptions CreateOptions(
        string? outputDirectoryRelativePathOverride = null,
        string? outputFileNameTemplateOverride = null)
        => new(
            ServiceKind: RadiCorder.Logics.Models.Enums.RadioServiceKind.Radiko,
            IsTimeFree: false,
            StartDelaySeconds: 0,
            EndDelaySeconds: 0,
            OutputDirectoryRelativePathOverride: outputDirectoryRelativePathOverride,
            OutputFileNameTemplateOverride: outputFileNameTemplateOverride);

    /// <summary>
    /// 設定モック生成
    /// </summary>
    private static Mock<IAppConfigurationService> CreateConfig(
        string recordFileSaveDir,
        string temporaryFileSaveDir,
        string? recordDirectoryRelativePath,
        string? recordFileNameTemplate)
    {
        var mock = new Mock<IAppConfigurationService>();
        mock.SetupGet(x => x.RecordFileSaveDir).Returns(recordFileSaveDir);
        mock.SetupGet(x => x.TemporaryFileSaveDir).Returns(temporaryFileSaveDir);
        mock.SetupGet(x => x.RecordDirectoryRelativePath).Returns(recordDirectoryRelativePath);
        mock.SetupGet(x => x.RecordFileNameTemplate).Returns(recordFileNameTemplate);
        return mock;
    }

    /// <summary>
    /// 一時ディレクトリを作成
    /// </summary>
    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "MediaStorageServiceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 例外を握ってディレクトリ削除
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
