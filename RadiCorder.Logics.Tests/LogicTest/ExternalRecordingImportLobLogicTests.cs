using RadiCorder.Logics.Infrastructure.Import;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Moq;
using RadiCorder.Logics.Logics.RecordedRadioLogic;
using RadiCorder.Logics.Logics.TagLogic;
using RadiCorder.Logics.Models.ExternalImport;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Tests.LogicTest;

/// <summary>
/// ExternalRecordingImportLobLogic のテスト
/// </summary>
public class ExternalRecordingImportLobLogicTests : UnitTestBase
{
    private string _rootPath = string.Empty;
    private ExternalRecordingImportLobLogic _logic = null!;
    private Mock<IAppConfigurationService> _configMock = null!;

    [SetUp]
    public void Setup()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"radicorder-import-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootPath);

        _configMock = new Mock<IAppConfigurationService>();
        _configMock.SetupGet(x => x.RecordFileSaveDir).Returns(_rootPath);
        _configMock.SetupGet(x => x.RecordDirectoryRelativePath).Returns(string.Empty);
        _configMock.SetupGet(x => x.RecordFileNameTemplate).Returns(string.Empty);

        var tagLogic = new TagLobLogic(new Mock<ILogger<TagLobLogic>>().Object, DbContext);
        _logic = new ExternalRecordingImportLobLogic(
            new Mock<ILogger<ExternalRecordingImportLobLogic>>().Object,
            _configMock.Object,
            tagLogic,
            DbContext);
    }

    [TearDown]
    public async Task TearDown()
    {
        DbContext.RecordingTagRelations.RemoveRange(DbContext.RecordingTagRelations);
        DbContext.RecordingTags.RemoveRange(DbContext.RecordingTags);
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
    public async Task ScanCandidatesAsync_未登録ファイルのみ返す()
    {
        var importTarget = Path.Combine(_rootPath, "import-target.mp3");
        await File.WriteAllBytesAsync(importTarget, [0x00, 0x01, 0x02]);

        var registered = Path.Combine(_rootPath, "registered.mp3");
        await File.WriteAllBytesAsync(registered, [0x03, 0x04, 0x05]);
        await AddRegisteredRecordingAsync(registered);

        var list = await _logic.ScanCandidatesAsync();

        var expectedRelativePath = Path.GetRelativePath(_rootPath, importTarget);
        Assert.That(list.Any(x => x.FilePath == expectedRelativePath), Is.True);
        var registeredRelativePath = Path.GetRelativePath(_rootPath, registered);
        Assert.That(list.Any(x => x.FilePath == registeredRelativePath), Is.False);
        Assert.That(list.Single(x => x.FilePath == expectedRelativePath).Tags, Is.EquivalentTo(new[] { "外部取込" }));
    }

    [Test]
    public async Task ScanCandidatesAsync_初期タグ付与オフならタグを付けない()
    {
        var target = Path.Combine(_rootPath, "no-default-tag.mp3");
        await File.WriteAllBytesAsync(target, [0x00, 0x01, 0x02]);

        var list = await _logic.ScanCandidatesAsync(false);

        var expectedRelativePath = Path.GetRelativePath(_rootPath, target);
        var candidate = list.Single(x => x.FilePath == expectedRelativePath);
        Assert.That(candidate.Tags, Is.Empty);
    }

    [Test]
    public async Task SaveCandidatesAsync_検証エラー時は一件も保存しない()
    {
        var target = Path.Combine(_rootPath, "missing-title.mp3");
        await File.WriteAllBytesAsync(target, [0x00, 0x01, 0x02]);

        var result = await _logic.SaveCandidatesAsync([
            new ExternalImportCandidateEntry
            {
                IsSelected = true,
                FilePath = Path.GetRelativePath(_rootPath, target),
                Title = string.Empty,
                Description = string.Empty,
                StationName = "不明",
                BroadcastAt = DateTimeOffset.UtcNow,
                Tags = ["外部取込"]
            }
        ]);

        Assert.That(result.Errors.Count, Is.GreaterThan(0));
        Assert.That(await DbContext.Recordings.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task ImportCandidatesCsvAsync_管理フォルダ外パスを拒否する()
    {
        var outside = Path.GetTempFileName();
        try
        {
            var csv = "FilePath,Title,Description,StationName,BroadcastAt,Tags\n" +
                      $"{outside},Title,Desc,Station,2026-01-01T00:00:00+00:00,外部取込\n";
            await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

            var (isSuccess, _, errors) = await _logic.ImportCandidatesCsvAsync(stream);

            Assert.That(isSuccess, Is.False);
            Assert.That(errors.Count, Is.GreaterThan(0));
        }
        finally
        {
            if (File.Exists(outside))
            {
                File.Delete(outside);
            }
        }
    }

    [Test]
    public async Task ImportCandidatesCsvAsync_改行を含むフィールドを取り込める()
    {
        var target = Path.Combine(_rootPath, "multiline.mp3");
        await File.WriteAllBytesAsync(target, [0x00, 0x01, 0x02]);
        var relative = Path.GetRelativePath(_rootPath, target);

        var csv = string.Join("\n",
            "FilePath,Title,Description,StationName,BroadcastAt,Tags",
            $"\"{relative}\",\"Title\",\"line1",
            $"line2\",\"Station\",\"2026-01-01T00:00:00+00:00\",\"外部取込\""
        );

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var (isSuccess, candidates, errors) = await _logic.ImportCandidatesCsvAsync(stream);

        Assert.That(isSuccess, Is.True);
        Assert.That(errors, Is.Empty);
        Assert.That(candidates.Count, Is.EqualTo(1));
        Assert.That(candidates[0].Description, Does.Contain("line1"));
        Assert.That(candidates[0].Description, Does.Contain("line2"));
    }

    [Test]
    public async Task ImportCandidatesCsvAsync_OS非依存セパレータでも取り込める()
    {
        var target = Path.Combine(_rootPath, "nested", "cross-separator.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target, [0x00, 0x01, 0x02]);

        var expectedRelative = Path.GetRelativePath(_rootPath, target);
        var otherSeparator = Path.DirectorySeparatorChar == '/' ? '\\' : '/';
        var csvRelative = expectedRelative.Replace(Path.DirectorySeparatorChar, otherSeparator);

        var csv = string.Join("\n",
            "FilePath,Title,Description,StationName,BroadcastAt,Tags",
            $"\"{csvRelative}\",\"Title\",\"Desc\",\"Station\",\"2026-01-01T00:00:00+00:00\",\"外部取込\""
        );

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var (isSuccess, candidates, errors) = await _logic.ImportCandidatesCsvAsync(stream);

        Assert.That(isSuccess, Is.True);
        Assert.That(errors, Is.Empty);
        Assert.That(candidates.Count, Is.EqualTo(1));
        Assert.That(candidates[0].FilePath, Is.EqualTo(expectedRelative));
    }

    [Test]
    public async Task ImportCandidatesCsvAsync_不正クォートは解析エラーとして返す()
    {
        var target = Path.Combine(_rootPath, "broken-quote.mp3");
        await File.WriteAllBytesAsync(target, [0x00, 0x01, 0x02]);
        var relative = Path.GetRelativePath(_rootPath, target);

        var csv = string.Join("\n",
            "FilePath,Title,Description,StationName,BroadcastAt,Tags",
            $"\"{relative}\",\"Title\",\"broken,\"Station\",\"2026-01-01T00:00:00+00:00\",\"外部取込\""
        );

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var (isSuccess, candidates, errors) = await _logic.ImportCandidatesCsvAsync(stream);

        Assert.That(isSuccess, Is.False);
        Assert.That(candidates, Is.Empty);
        Assert.That(errors, Is.Not.Empty);
    }

    [Test]
    public async Task ImportCandidatesCsvAsync_ディレクトリトラバーサルは拒否する()
    {
        var csv = string.Join("\n",
            "FilePath,Title,Description,StationName,BroadcastAt,Tags",
            "\"..\\\\..\\\\evil.mp3\",\"Title\",\"Desc\",\"Station\",\"2026-01-01T00:00:00+00:00\",\"外部取込\""
        );

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var (isSuccess, candidates, errors) = await _logic.ImportCandidatesCsvAsync(stream);

        Assert.That(isSuccess, Is.False);
        Assert.That(candidates, Is.Empty);
        Assert.That(errors, Is.Not.Empty);
        Assert.That(errors.Any(x => x.Contains("ファイルパスが不正", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void TryResolveManagedFilePath_UNCルートでも相対パスを解決できる()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Pass("UNC パスの検証は Windows のみ実行します。");
            return;
        }

        var result = ExternalImportPaths.TryResolveManagedFilePath(
            "nested/file.mp3", @"\\server\share\record", out var fullPath, out var relativePath);
        Assert.That(result, Is.True);
        Assert.That(fullPath, Does.EndWith(@"record\nested\file.mp3"));
        Assert.That(relativePath, Is.EqualTo(@"nested\file.mp3"));
    }

    [Test]
    public async Task SaveCandidatesAsync_未登録タグが含まれる場合は保存しない()
    {
        var target = Path.Combine(_rootPath, "unknown-tag.mp3");
        await File.WriteAllBytesAsync(target, [0x00, 0x01, 0x02]);

        var result = await _logic.SaveCandidatesAsync([
            new ExternalImportCandidateEntry
            {
                IsSelected = true,
                FilePath = Path.GetRelativePath(_rootPath, target),
                Title = "UnknownTag",
                Description = string.Empty,
                StationName = "不明",
                BroadcastAt = DateTimeOffset.UtcNow,
                Tags = ["未登録タグ"]
            }
        ]);

        Assert.That(result.Errors.Any(x => x.Message.Contains("未登録タグ", StringComparison.Ordinal)), Is.True);
        Assert.That(await DbContext.Recordings.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task ScanCandidatesAsync_テンプレート一致時は放送局名と放送日時を補完する()
    {
        _configMock.SetupGet(x => x.RecordDirectoryRelativePath).Returns("$StationName$/$Title$/$SYYYY$/$SMM$/");
        _configMock.SetupGet(x => x.RecordFileNameTemplate).Returns("$SYYYY$$SMM$$SDD$_$Title$");

        var relativePath = Path.Combine("BUNKA", "MyProgram", "2026", "02", "20260213_MyProgram.mp3");
        var fullPath = Path.Combine(_rootPath, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(fullPath, [0x00, 0x01]);

        var list = await _logic.ScanCandidatesAsync();
        var candidate = list.Single();

        Assert.That(candidate.StationName, Is.EqualTo("BUNKA"));
        Assert.That(candidate.Title, Is.EqualTo("MyProgram"));
        Assert.That(candidate.BroadcastAt.Year, Is.EqualTo(2026));
        Assert.That(candidate.BroadcastAt.Month, Is.EqualTo(2));
        Assert.That(candidate.BroadcastAt.Day, Is.EqualTo(13));
    }

    [Test]
    public async Task ScanCandidatesAsync_ファイル名テンプレート不一致でもフォルダ一致なら放送局名を補完する()
    {
        _configMock.SetupGet(x => x.RecordDirectoryRelativePath).Returns("$StationName$/$Title$/$SYYYY$/");
        _configMock.SetupGet(x => x.RecordFileNameTemplate).Returns("$SYYYY$$SMM$$SDD$_$Title$");

        var relativePath = Path.Combine("AIR-G'（FM北海道）", "FM EVA 30.0", "2025", "20251129223000_FM EVA 30.0.m4a");
        var fullPath = Path.Combine(_rootPath, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(fullPath, [0x00, 0x01]);

        var list = await _logic.ScanCandidatesAsync();
        var candidate = list.Single();

        Assert.That(candidate.StationName, Is.EqualTo("AIR-G'（FM北海道）"));
        Assert.That(candidate.Title, Is.EqualTo("FM EVA 30.0"));
        Assert.That(candidate.BroadcastAt.Year, Is.EqualTo(2025));
        Assert.That(candidate.BroadcastAt.Month, Is.EqualTo(11));
        Assert.That(candidate.BroadcastAt.Day, Is.EqualTo(29));
        Assert.That(candidate.BroadcastAt.Hour, Is.EqualTo(22));
        Assert.That(candidate.BroadcastAt.Minute, Is.EqualTo(30));
        Assert.That(candidate.BroadcastAt.Second, Is.EqualTo(0));
    }

    [Test]
    public async Task ScanCandidatesAsync_フォルダテンプレートが一部不一致でも先頭階層から放送局名を補完する()
    {
        _configMock.SetupGet(x => x.RecordDirectoryRelativePath).Returns("$StationName$/$Title$/$SYYYY$/$SMM$/");
        _configMock.SetupGet(x => x.RecordFileNameTemplate).Returns("$SYYYY$$SMM$$SDD$$STHH$$STMM$$STSS$_$Title$");

        // $SMM$ フォルダが欠けていてディレクトリ全体のテンプレート一致は失敗するが、
        // 先頭階層の $StationName$ から放送局名を補完できることを確認する。
        var relativePath = Path.Combine("AIR-G'（FM北海道）", "FM EVA 30.0", "2025", "20251011223000_FM EVA 30.0.m4a");
        var fullPath = Path.Combine(_rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, [0x00, 0x01]);

        var list = await _logic.ScanCandidatesAsync();
        var candidate = list.Single();

        Assert.That(candidate.StationName, Is.EqualTo("AIR-G'（FM北海道）"));
        Assert.That(candidate.Title, Is.EqualTo("FM EVA 30.0"));
        Assert.That(candidate.BroadcastAt.Year, Is.EqualTo(2025));
        Assert.That(candidate.BroadcastAt.Month, Is.EqualTo(10));
        Assert.That(candidate.BroadcastAt.Day, Is.EqualTo(11));
        Assert.That(candidate.BroadcastAt.Hour, Is.EqualTo(22));
        Assert.That(candidate.BroadcastAt.Minute, Is.EqualTo(30));
        Assert.That(candidate.BroadcastAt.Second, Is.EqualTo(0));
    }

    [Test]
    public async Task ScanCandidatesAsync_ファイル名テンプレート未設定でも先頭階層から放送局名を補完する()
    {
        _configMock.SetupGet(x => x.RecordDirectoryRelativePath).Returns("$StationName$/$Title$/$SYYYY$/$SMM$/");
        _configMock.SetupGet(x => x.RecordFileNameTemplate).Returns(string.Empty);

        // 設定上は $SMM$ が必要だが、実ファイルは月フォルダなし。
        // それでも $StationName$ / $Title$ を補完できることを保証する。
        var relativePath = Path.Combine("AIR-G'（FM北海道）", "FM EVA 30.0", "2025", "20251011223000_FM EVA 30.0.m4a");
        var fullPath = Path.Combine(_rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, [0x00, 0x01]);

        var list = await _logic.ScanCandidatesAsync();
        var candidate = list.Single();

        Assert.That(candidate.StationName, Is.EqualTo("AIR-G'（FM北海道）"));
        Assert.That(candidate.Title, Is.EqualTo("FM EVA 30.0"));
        Assert.That(candidate.BroadcastAt.Year, Is.EqualTo(2025));
        Assert.That(candidate.BroadcastAt.Month, Is.EqualTo(10));
        Assert.That(candidate.BroadcastAt.Day, Is.EqualTo(11));
        Assert.That(candidate.BroadcastAt.Hour, Is.EqualTo(22));
        Assert.That(candidate.BroadcastAt.Minute, Is.EqualTo(30));
        Assert.That(candidate.BroadcastAt.Second, Is.EqualTo(0));
    }

    private async ValueTask AddRegisteredRecordingAsync(string fullPath)
    {
        var id = Ulid.NewUlid();
        await DbContext.Recordings.AddAsync(new Recording
        {
            Id = id,
            ServiceKind = Models.Enums.RadioServiceKind.Radiko,
            ProgramId = $"P-{id}",
            StationId = "TBS",
            AreaId = "JP13",
            StartDateTime = DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime,
            EndDateTime = DateTimeOffset.UtcNow.UtcDateTime,
            IsTimeFree = false,
            State = Domain.Recording.RecordingState.Completed,
            ErrorMessage = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            SourceType = Domain.Recording.RecordingSourceType.Recorded
        });

        await DbContext.RecordingMetadatas.AddAsync(new RecordingMetadata
        {
            RecordingId = id,
            StationName = "TBS",
            Title = "Registered",
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
    }
}
