using Microsoft.Extensions.Logging;
using Moq;
using RadiCorder.Logics.Domain.AppEvent;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Tests.Mocks;
using RadiCorder.Logics.UseCases.Recording;

namespace RadiCorder.Logics.Tests.LogicTest;

public class RecordingOrchestratorTests
{
    /// <summary>
    /// 正常系: 録音が成功し、状態がCompletedになること
    /// </summary>
    [Test]
    public async Task RecordAsync_Success_ReturnsCompleted()
    {
        var logger = new Mock<ILogger<RecordingOrchestrator>>().Object;

        var metadata = new ProgramRecordingInfo(
            ProgramId: "P1",
            Title: "Test",
            Subtitle: "",
            StationId: "ST",
            StationName: "Station",
            AreaId: "AR",
            StartTime: DateTimeOffset.UtcNow,
            EndTime: DateTimeOffset.UtcNow.AddMinutes(30),
            Performer: "P",
            Description: "D",
            ProgramUrl: "");

        var options = new RecordingOptions(
            ServiceKind: RadioServiceKind.Radiko,
            IsTimeFree: false,
            StartDelaySeconds: 0,
            EndDelaySeconds: 0);

        var sourceResult = new RecordingSourceResult(
            StreamUrl: "http://example/stream.m3u8",
            Headers: new Dictionary<string, string>(),
            ProgramInfo: metadata,
            Options: options);

        var source = new FakeRecordingSource(RadioServiceKind.Radiko, sourceResult);
        var storage = new FakeMediaStorageService();
        var transcoder = new FakeMediaTranscodeService(true);
        var repo = new InMemoryRecordingRepository();
        var publisher = new Mock<IRecordingStateEventPublisher>().Object;

        var orchestrator = new RecordingOrchestrator(
            logger,
            new[] { source },
            storage,
            transcoder,
            repo,
            publisher);

        var command = new RecordingCommand(
            ServiceKind: RadioServiceKind.Radiko,
            ProgramId: "P1",
            ProgramName: "Test",
            IsTimeFree: false,
            StartDelaySeconds: 0,
            EndDelaySeconds: 0);

        var result = await orchestrator.RecordAsync(command);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.RecordingId, Is.Not.Null);
        Assert.That(storage.IsCommitCalled, Is.True);
        Assert.That(storage.IsCleanupCalled, Is.False);

        var id = result.RecordingId!.Value;
        Assert.That(repo.Store[id].State, Is.EqualTo(RecordingState.Completed));
    }

    /// <summary>
    /// 正常系: コミット時に最終パスが変更された場合、リポジトリへ変更後パスが反映されること
    /// </summary>
    [Test]
    public async Task RecordAsync_Commitでリネーム_変更後パスが保存される()
    {
        var logger = new Mock<ILogger<RecordingOrchestrator>>().Object;

        var metadata = new ProgramRecordingInfo(
            ProgramId: "P1",
            Title: "Test",
            Subtitle: "",
            StationId: "ST",
            StationName: "Station",
            AreaId: "AR",
            StartTime: DateTimeOffset.UtcNow,
            EndTime: DateTimeOffset.UtcNow.AddMinutes(30),
            Performer: "P",
            Description: "D",
            ProgramUrl: "");

        var options = new RecordingOptions(
            ServiceKind: RadioServiceKind.Radiko,
            IsTimeFree: false,
            StartDelaySeconds: 0,
            EndDelaySeconds: 0);

        var sourceResult = new RecordingSourceResult(
            StreamUrl: "http://example/stream.m3u8",
            Headers: new Dictionary<string, string>(),
            ProgramInfo: metadata,
            Options: options);

        var source = new FakeRecordingSource(RadioServiceKind.Radiko, sourceResult);
        var storage = new RenameOnCommitMediaStorageService(
            prepared: new MediaPath("temp.m4a", "record/Test.m4a", "record/Test.m4a"),
            committed: new MediaPath("temp.m4a", "record/Test_duplicate_123.m4a", "record/Test_duplicate_123.m4a"));
        var transcoder = new FakeMediaTranscodeService(true);
        var repo = new InMemoryRecordingRepository();
        var publisher = new Mock<IRecordingStateEventPublisher>().Object;

        var orchestrator = new RecordingOrchestrator(
            logger,
            new[] { source },
            storage,
            transcoder,
            repo,
            publisher);

        var command = new RecordingCommand(
            ServiceKind: RadioServiceKind.Radiko,
            ProgramId: "P1",
            ProgramName: "Test",
            IsTimeFree: false,
            StartDelaySeconds: 0,
            EndDelaySeconds: 0);

        var result = await orchestrator.RecordAsync(command);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.RecordingId, Is.Not.Null);

        var id = result.RecordingId!.Value;
        Assert.That(repo.Store[id].Path.FinalFilePath, Is.EqualTo("record/Test_duplicate_123.m4a"));
        Assert.That(repo.Store[id].Path.RelativePath, Is.EqualTo("record/Test_duplicate_123.m4a"));
    }

    /// <summary>
    /// 異常系: 録音失敗時に状態がFailedになること
    /// </summary>
    [Test]
    public async Task RecordAsync_TranscodeFail_ReturnsFailed()
    {
        var logger = new Mock<ILogger<RecordingOrchestrator>>().Object;

        var metadata = new ProgramRecordingInfo(
            ProgramId: "P2",
            Title: "Test",
            Subtitle: "",
            StationId: "ST",
            StationName: "Station",
            AreaId: "AR",
            StartTime: DateTimeOffset.UtcNow,
            EndTime: DateTimeOffset.UtcNow.AddMinutes(30),
            Performer: "P",
            Description: "D",
            ProgramUrl: "");

        var options = new RecordingOptions(
            ServiceKind: RadioServiceKind.Radiko,
            IsTimeFree: false,
            StartDelaySeconds: 0,
            EndDelaySeconds: 0);

        var sourceResult = new RecordingSourceResult(
            StreamUrl: "http://example/stream.m3u8",
            Headers: new Dictionary<string, string>(),
            ProgramInfo: metadata,
            Options: options);

        var source = new FakeRecordingSource(RadioServiceKind.Radiko, sourceResult);
        var storage = new FakeMediaStorageService();
        var transcoder = new FakeMediaTranscodeService(false);
        var repo = new InMemoryRecordingRepository();
        var publisher = new Mock<IRecordingStateEventPublisher>().Object;

        var orchestrator = new RecordingOrchestrator(
            logger,
            new[] { source },
            storage,
            transcoder,
            repo,
            publisher);

        var command = new RecordingCommand(
            ServiceKind: RadioServiceKind.Radiko,
            ProgramId: "P2",
            ProgramName: "Test",
            IsTimeFree: false,
            StartDelaySeconds: 0,
            EndDelaySeconds: 0);

        var result = await orchestrator.RecordAsync(command);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.RecordingId, Is.Not.Null);
        Assert.That(storage.IsCommitCalled, Is.False);
        Assert.That(storage.IsCleanupCalled, Is.True);

        var id = result.RecordingId!.Value;
        Assert.That(repo.Store[id].State, Is.EqualTo(RecordingState.Failed));
    }

    /// <summary>
    /// 異常系: 録音失敗時に失敗トーストが配信されること
    /// </summary>
    [Test]
    public async Task RecordAsync_TranscodeFail_失敗トーストを配信()
    {
        var logger = new Mock<ILogger<RecordingOrchestrator>>().Object;

        var metadata = new ProgramRecordingInfo(
            ProgramId: "P2",
            Title: "Test",
            Subtitle: "",
            StationId: "ST",
            StationName: "Station",
            AreaId: "AR",
            StartTime: DateTimeOffset.UtcNow,
            EndTime: DateTimeOffset.UtcNow.AddMinutes(30),
            Performer: "P",
            Description: "D",
            ProgramUrl: "");

        var options = new RecordingOptions(
            ServiceKind: RadioServiceKind.Radiko,
            IsTimeFree: false,
            StartDelaySeconds: 0,
            EndDelaySeconds: 0);

        var sourceResult = new RecordingSourceResult(
            StreamUrl: "http://example/stream.m3u8",
            Headers: new Dictionary<string, string>(),
            ProgramInfo: metadata,
            Options: options);

        var source = new FakeRecordingSource(RadioServiceKind.Radiko, sourceResult);
        var storage = new FakeMediaStorageService();
        var transcoder = new FakeMediaTranscodeService(false);
        var repo = new InMemoryRecordingRepository();
        var publisher = new Mock<IRecordingStateEventPublisher>().Object;
        var appToastEventPublisher = new Mock<IAppToastEventPublisher>();

        var orchestrator = new RecordingOrchestrator(
            logger,
            new[] { source },
            storage,
            transcoder,
            repo,
            publisher,
            appToastEventPublisher.Object);

        var command = new RecordingCommand(
            ServiceKind: RadioServiceKind.Radiko,
            ProgramId: "P2",
            ProgramName: "Test",
            IsTimeFree: false,
            StartDelaySeconds: 0,
            EndDelaySeconds: 0);

        var result = await orchestrator.RecordAsync(command);

        Assert.That(result.IsSuccess, Is.False);
        appToastEventPublisher.Verify(
            x => x.PublishAsync(
                It.Is<AppToastEvent>(p =>
                    p.Message == "Test の録音に失敗しました。理由: 録音処理に失敗しました。"
                    && p.IsSuccess == false),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// 異常系: 対応ソースが存在しない場合は失敗を返す
    /// </summary>
    [Test]
    public async Task RecordAsync_NoSource_ReturnsFailed()
    {
        var logger = new Mock<ILogger<RecordingOrchestrator>>().Object;

        var storage = new FakeMediaStorageService();
        var transcoder = new FakeMediaTranscodeService(true);
        var repo = new InMemoryRecordingRepository();
        var publisher = new Mock<IRecordingStateEventPublisher>().Object;

        var orchestrator = new RecordingOrchestrator(
            logger,
            Array.Empty<IRecordingSource>(),
            storage,
            transcoder,
            repo,
            publisher);

        var command = new RecordingCommand(
            ServiceKind: RadioServiceKind.Radiko,
            ProgramId: "P3",
            ProgramName: "Test",
            IsTimeFree: false,
            StartDelaySeconds: 0,
            EndDelaySeconds: 0);

        var result = await orchestrator.RecordAsync(command);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.RecordingId, Is.Null);
        Assert.That(storage.IsCommitCalled, Is.False);
        Assert.That(storage.IsCleanupCalled, Is.False);
    }

    /// <summary>
    /// 異常系: ソース準備が失敗した場合は失敗を返す
    /// </summary>
    [Test]
    public async Task RecordAsync_PrepareThrows_ReturnsFailed()
    {
        var logger = new Mock<ILogger<RecordingOrchestrator>>().Object;

        var metadata = new ProgramRecordingInfo(
            ProgramId: "P4",
            Title: "Test",
            Subtitle: "",
            StationId: "ST",
            StationName: "Station",
            AreaId: "AR",
            StartTime: DateTimeOffset.UtcNow,
            EndTime: DateTimeOffset.UtcNow.AddMinutes(30),
            Performer: "P",
            Description: "D",
            ProgramUrl: "");

        var options = new RecordingOptions(
            ServiceKind: RadioServiceKind.Radiko,
            IsTimeFree: false,
            StartDelaySeconds: 0,
            EndDelaySeconds: 0);

        var sourceResult = new RecordingSourceResult(
            StreamUrl: "http://example/stream.m3u8",
            Headers: new Dictionary<string, string>(),
            ProgramInfo: metadata,
            Options: options);

        var source = new FakeRecordingSource(
            RadioServiceKind.Radiko,
            sourceResult,
            new DomainException("準備に失敗しました。"));

        var storage = new FakeMediaStorageService();
        var transcoder = new FakeMediaTranscodeService(true);
        var repo = new InMemoryRecordingRepository();
        var publisher = new Mock<IRecordingStateEventPublisher>().Object;

        var orchestrator = new RecordingOrchestrator(
            logger,
            new[] { source },
            storage,
            transcoder,
            repo,
            publisher);

        var command = new RecordingCommand(
            ServiceKind: RadioServiceKind.Radiko,
            ProgramId: "P4",
            ProgramName: "Test",
            IsTimeFree: false,
            StartDelaySeconds: 0,
            EndDelaySeconds: 0);

        var result = await orchestrator.RecordAsync(command);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.RecordingId, Is.Null);
        Assert.That(storage.IsCommitCalled, Is.False);
        Assert.That(storage.IsCleanupCalled, Is.False);
    }

    /// <summary>
    /// 異常系: Commit失敗時は退避保存され、Cleanupは実行されない
    /// </summary>
    [Test]
    public async Task RecordAsync_CommitThrows_退避保存してCleanupしない()
    {
        var logger = new Mock<ILogger<RecordingOrchestrator>>().Object;

        var metadata = new ProgramRecordingInfo(
            ProgramId: "P5",
            Title: "Test",
            Subtitle: "",
            StationId: "ST",
            StationName: "Station",
            AreaId: "AR",
            StartTime: DateTimeOffset.UtcNow,
            EndTime: DateTimeOffset.UtcNow.AddMinutes(30),
            Performer: "P",
            Description: "D",
            ProgramUrl: "");

        var options = new RecordingOptions(
            ServiceKind: RadioServiceKind.Radiko,
            IsTimeFree: false,
            StartDelaySeconds: 0,
            EndDelaySeconds: 0);

        var sourceResult = new RecordingSourceResult(
            StreamUrl: "http://example/stream.m3u8",
            Headers: new Dictionary<string, string>(),
            ProgramInfo: metadata,
            Options: options);

        var source = new FakeRecordingSource(RadioServiceKind.Radiko, sourceResult);
        var storage = new ThrowingMediaStorageService();
        var transcoder = new FakeMediaTranscodeService(true);
        var repo = new InMemoryRecordingRepository();
        var publisher = new Mock<IRecordingStateEventPublisher>().Object;

        var orchestrator = new RecordingOrchestrator(
            logger,
            new[] { source },
            storage,
            transcoder,
            repo,
            publisher);

        var command = new RecordingCommand(
            ServiceKind: RadioServiceKind.Radiko,
            ProgramId: "P5",
            ProgramName: "Test",
            IsTimeFree: false,
            StartDelaySeconds: 0,
            EndDelaySeconds: 0);

        var result = await orchestrator.RecordAsync(command);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(storage.IsSaveFailedCalled, Is.True);
        Assert.That(storage.IsCleanupCalled, Is.False);
        Assert.That(result.ErrorMessage, Is.EqualTo("録音は完了しましたが、保存先エラーのため正式保存できませんでした（退避済み）"));
    }

    /// <summary>
    /// 異常系: 状態更新が失敗しても処理は継続する
    /// </summary>
    [Test]
    public async Task RecordAsync_UpdateStateThrows_処理継続()
    {
        var logger = new Mock<ILogger<RecordingOrchestrator>>().Object;

        var metadata = new ProgramRecordingInfo(
            ProgramId: "P6",
            Title: "Test",
            Subtitle: "",
            StationId: "ST",
            StationName: "Station",
            AreaId: "AR",
            StartTime: DateTimeOffset.UtcNow,
            EndTime: DateTimeOffset.UtcNow.AddMinutes(30),
            Performer: "P",
            Description: "D",
            ProgramUrl: "");

        var options = new RecordingOptions(
            ServiceKind: RadioServiceKind.Radiko,
            IsTimeFree: false,
            StartDelaySeconds: 0,
            EndDelaySeconds: 0);

        var sourceResult = new RecordingSourceResult(
            StreamUrl: "http://example/stream.m3u8",
            Headers: new Dictionary<string, string>(),
            ProgramInfo: metadata,
            Options: options);

        var source = new FakeRecordingSource(RadioServiceKind.Radiko, sourceResult);
        var storage = new FakeMediaStorageService();
        var transcoder = new FakeMediaTranscodeService(false);
        var repo = new ThrowingUpdateStateRepository();
        var publisher = new Mock<IRecordingStateEventPublisher>().Object;

        var orchestrator = new RecordingOrchestrator(
            logger,
            new[] { source },
            storage,
            transcoder,
            repo,
            publisher);

        var command = new RecordingCommand(
            ServiceKind: RadioServiceKind.Radiko,
            ProgramId: "P6",
            ProgramName: "Test",
            IsTimeFree: false,
            StartDelaySeconds: 0,
            EndDelaySeconds: 0);

        var result = await orchestrator.RecordAsync(command);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.RecordingId, Is.Not.Null);
    }

    /// <summary>
    /// 正常系: タイムフリー録音失敗時は認証キャッシュ破棄後に1回だけ再試行する
    /// </summary>
    [Test]
    public async Task RecordAsync_TimeFreeFailThenRetry_再試行で成功する()
    {
        var logger = new Mock<ILogger<RecordingOrchestrator>>().Object;

        var metadata = new ProgramRecordingInfo(
            ProgramId: "P7",
            Title: "Test",
            Subtitle: "",
            StationId: "ST",
            StationName: "Station",
            AreaId: "AR",
            StartTime: DateTimeOffset.UtcNow,
            EndTime: DateTimeOffset.UtcNow.AddMinutes(30),
            Performer: "P",
            Description: "D",
            ProgramUrl: "");

        var options = new RecordingOptions(
            ServiceKind: RadioServiceKind.Radiko,
            IsTimeFree: true,
            StartDelaySeconds: 0,
            EndDelaySeconds: 0);

        var sourceResult = new RecordingSourceResult(
            StreamUrl: "http://example/stream.m3u8",
            Headers: new Dictionary<string, string>(),
            ProgramInfo: metadata,
            Options: options);

        var source = new RetryAwareRecordingSource(RadioServiceKind.Radiko, sourceResult);
        var storage = new FakeMediaStorageService();
        var transcoder = new Mock<IMediaTranscodeService>();
        transcoder.SetupSequence(x => x.RecordAsync(It.IsAny<RecordingSourceResult>(), It.IsAny<MediaPath>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        var repo = new InMemoryRecordingRepository();
        var publisher = new Mock<IRecordingStateEventPublisher>().Object;

        var orchestrator = new RecordingOrchestrator(
            logger,
            new IRecordingSource[] { source },
            storage,
            transcoder.Object,
            repo,
            publisher);

        var command = new RecordingCommand(
            ServiceKind: RadioServiceKind.Radiko,
            ProgramId: "P7",
            ProgramName: "Test",
            IsTimeFree: true,
            StartDelaySeconds: 0,
            EndDelaySeconds: 0);

        var result = await orchestrator.RecordAsync(command);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(source.PrepareCallCount, Is.EqualTo(2));
        Assert.That(source.RetryPreparationCount, Is.EqualTo(1));
        Assert.That(storage.IsCleanupCalled, Is.True);
        transcoder.Verify(x => x.RecordAsync(It.IsAny<RecordingSourceResult>(), It.IsAny<MediaPath>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private sealed class ThrowingMediaStorageService : IMediaStorageService
    {
        public bool IsCleanupCalled { get; private set; }
        public bool IsSaveFailedCalled { get; private set; }

        public ValueTask<MediaPath> PrepareAsync(ProgramRecordingInfo programInfo, RecordingOptions options, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new MediaPath("temp.m4a", "final.m4a", "rel\\final.m4a"));

        public ValueTask<MediaPath> CommitAsync(MediaPath path, CancellationToken cancellationToken = default)
            => throw new Exception("commit fail");

        public ValueTask CleanupTempAsync(MediaPath path, CancellationToken cancellationToken = default)
        {
            IsCleanupCalled = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask<SaveFailedFallbackResult> SaveFailedAsync(
            MediaPath path,
            SaveFailedFallbackMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            IsSaveFailedCalled = true;
            return ValueTask.FromResult(new SaveFailedFallbackResult("save-failed\\fallback.m4a", "save-failed\\fallback.m4a.meta.json"));
        }
    }

    private sealed class ThrowingUpdateStateRepository : IRecordingRepository
    {
        public ValueTask<Ulid> CreateAsync(ProgramRecordingInfo programInfo, MediaPath path, RecordingOptions options, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Ulid.NewUlid());

        public ValueTask UpdateStateAsync(Ulid recordingId, RecordingState state, string? errorMessage = null, CancellationToken cancellationToken = default)
            => throw new Exception("update fail");

        public ValueTask UpdateFilePathAsync(Ulid recordingId, MediaPath path, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class RenameOnCommitMediaStorageService(MediaPath prepared, MediaPath committed) : IMediaStorageService
    {
        public ValueTask<MediaPath> PrepareAsync(ProgramRecordingInfo programInfo, RecordingOptions options, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(prepared);

        public ValueTask<MediaPath> CommitAsync(MediaPath path, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(committed);

        public ValueTask CleanupTempAsync(MediaPath path, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<SaveFailedFallbackResult> SaveFailedAsync(
            MediaPath path,
            SaveFailedFallbackMetadata metadata,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new SaveFailedFallbackResult("save-failed\\fallback.m4a", "save-failed\\fallback.m4a.meta.json"));
    }

    private sealed class RetryAwareRecordingSource(RadioServiceKind kind, RecordingSourceResult result)
        : IRecordingSource, IRecordingSourceRetryHandler
    {
        public int PrepareCallCount { get; private set; }
        public int RetryPreparationCount { get; private set; }

        public bool CanHandle(RadioServiceKind serviceKind) => serviceKind == kind;

        public ValueTask<RecordingSourceResult> PrepareAsync(RecordingCommand command, CancellationToken cancellationToken = default)
        {
            PrepareCallCount++;
            return ValueTask.FromResult(result);
        }

        public ValueTask PrepareForRetryAsync(RecordingCommand command, CancellationToken cancellationToken = default)
        {
            RetryPreparationCount++;
            return ValueTask.CompletedTask;
        }
    }
}
