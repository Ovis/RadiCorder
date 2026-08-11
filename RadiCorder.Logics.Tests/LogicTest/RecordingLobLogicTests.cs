using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Infrastructure.Notification;
using RadiCorder.Logics.Infrastructure.Recording;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Logics.RecordingLogic;
using RadiCorder.Logics.Logics.TagLogic;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.Tests.Mocks;
using RadiCorder.Logics.UseCases.Recording;

namespace RadiCorder.Logics.Tests.LogicTest;

public class RecordingLobLogicTests : UnitTestBase
{
    private static IAppConfigurationService CreateAppConfigurationService(bool mergeTagsFromAllMatchedKeywordRules = false)
    {
        var mock = new Mock<IAppConfigurationService>();
        mock.SetupGet(x => x.MergeTagsFromAllMatchedKeywordRules).Returns(mergeTagsFromAllMatchedKeywordRules);
        return mock.Object;
    }

    private NotificationLobLogic CreateNotificationLobLogic()
    {
        var logger = new Mock<ILogger<NotificationLobLogic>>().Object;
        var appContext = new FakeRadioAppContext();

        var configMock = new Mock<IAppConfigurationService>();
        configMock.SetupGet(c => c.DiscordWebhookUrl).Returns(string.Empty);
        configMock.SetupGet(c => c.NoticeCategories).Returns([]);
        configMock.SetupGet(c => c.RadikoStationDic).Returns(new System.Collections.Concurrent.ConcurrentDictionary<string, string>());

        var entryMapper = new EntryMapper(configMock.Object);
        var httpClientFactory = new FakeHttpClientFactory(new HttpClient(new FakeHttpMessageHandler()));
        var notificationRepository = new FakeNotificationRepository();

        return new NotificationLobLogic(
            logger,
            appContext,
            configMock.Object,
            entryMapper,
            httpClientFactory,
            notificationRepository);
    }

    private NotificationLobLogic CreateDbNotificationLobLogic()
    {
        var logger = new Mock<ILogger<NotificationLobLogic>>().Object;
        var appContext = new FakeRadioAppContext();

        var configMock = new Mock<IAppConfigurationService>();
        configMock.SetupGet(c => c.DiscordWebhookUrl).Returns(string.Empty);
        configMock.SetupGet(c => c.NoticeCategories).Returns([]);
        configMock.SetupGet(c => c.RadikoStationDic).Returns(new System.Collections.Concurrent.ConcurrentDictionary<string, string>());

        var entryMapper = new EntryMapper(configMock.Object);
        var httpClientFactory = new FakeHttpClientFactory(new HttpClient(new FakeHttpMessageHandler()));
        var notificationRepository = new NotificationRepository(DbContext);

        return new NotificationLobLogic(
            logger,
            appContext,
            configMock.Object,
            entryMapper,
            httpClientFactory,
            notificationRepository);
    }

    private static RecordingOrchestrator CreateOrchestrator(bool transcodeResult)
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
        var transcoder = new FakeMediaTranscodeService(transcodeResult);
        var repo = new InMemoryRecordingRepository();
        var publisher = new Mock<IRecordingStateEventPublisher>().Object;

        return new RecordingOrchestrator(
            logger,
            new[] { source },
            storage,
            transcoder,
            repo,
            publisher);
    }

    private RecordingOrchestrator CreateDbOrchestrator(bool transcodeResult)
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
        var transcoder = new FakeMediaTranscodeService(transcodeResult);
        var repo = new RecordingRepository(new Mock<ILogger<RecordingRepository>>().Object, DbContext);
        var publisher = new Mock<IRecordingStateEventPublisher>().Object;

        return new RecordingOrchestrator(
            logger,
            new[] { source },
            storage,
            transcoder,
            repo,
            publisher);
    }

    private static ScheduleJob CreateScheduleJob(Ulid id)
    {
        return new ScheduleJob
        {
            Id = id,
            KeywordReserveId = null,
            ServiceKind = RadioServiceKind.Radiko,
            StationId = "ST",
            AreaId = "AR",
            ProgramId = "P1",
            Title = "Test",
            Subtitle = string.Empty,
            FilePath = string.Empty,
            StartDateTime = DateTimeOffset.UtcNow,
            EndDateTime = DateTimeOffset.UtcNow.AddMinutes(30),
            StartDelay = TimeSpan.Zero,
            EndDelay = TimeSpan.Zero,
            Performer = string.Empty,
            Description = string.Empty,
            RecordingType = RecordingType.RealTime,
            ReserveType = ReserveType.Program,
            IsEnabled = true
        };
    }

    /// <summary>
    /// 正常系: 録音成功時にスケジュールが削除される
    /// </summary>
    [Test]
    public async Task RecordRadioAsync_Success_DeletesScheduleJob()
    {
        var scheduleJobId = Ulid.NewUlid();
        DbContext.ScheduleJob.Add(CreateScheduleJob(scheduleJobId));
        await DbContext.SaveChangesAsync();

        var logger = new Mock<ILogger<RecordingLobLogic>>().Object;
        var orchestrator = CreateOrchestrator(transcodeResult: true);
        var notificationLobLogic = CreateNotificationLobLogic();

        var logic = new RecordingLobLogic(
            logger,
            orchestrator,
            DbContext,
            notificationLobLogic,
            CreateAppConfigurationService(),
            new TagLobLogic(new Mock<ILogger<TagLobLogic>>().Object, DbContext));

        var (isSuccess, error) = await logic.RecordRadioAsync(
            serviceKind: RadioServiceKind.Radiko,
            programId: "P1",
            programName: "Test",
            scheduleJobId: scheduleJobId.ToString(),
            isTimeFree: false,
            startDelay: 0,
            endDelay: 0);

        Assert.That(isSuccess, Is.True);
        Assert.That(error, Is.Null);

        var deleted = await DbContext.ScheduleJob.FindAsync(scheduleJobId);
        Assert.That(deleted, Is.Null);
    }

    [Test]
    public async Task RecordRadioAsync_Success_お知らせに録音完了が登録される()
    {
        var scheduleJobId = Ulid.NewUlid();
        DbContext.ScheduleJob.Add(CreateScheduleJob(scheduleJobId));
        await DbContext.SaveChangesAsync();

        var logic = new RecordingLobLogic(
            new Mock<ILogger<RecordingLobLogic>>().Object,
            CreateDbOrchestrator(transcodeResult: true),
            DbContext,
            CreateDbNotificationLobLogic(),
            CreateAppConfigurationService(),
            new TagLobLogic(new Mock<ILogger<TagLobLogic>>().Object, DbContext));

        var (isSuccess, error) = await logic.RecordRadioAsync(
            serviceKind: RadioServiceKind.Radiko,
            programId: "P1",
            programName: "Test",
            scheduleJobId: scheduleJobId.ToString(),
            isTimeFree: false,
            startDelay: 0,
            endDelay: 0);

        Assert.That(isSuccess, Is.True);
        Assert.That(error, Is.Null);

        var notification = await DbContext.Notification
            .OrderByDescending(x => x.Timestamp)
            .FirstOrDefaultAsync();

        Assert.That(notification, Is.Not.Null);
        Assert.That(notification!.Category, Is.EqualTo(NoticeCategory.RecordingSuccess));
        Assert.That(notification.Message, Is.EqualTo("Test の録音が完了しました。"));
    }

    /// <summary>
    /// 異常系: 録音失敗時は失敗を返す
    /// </summary>
    [Test]
    public async Task RecordRadioAsync_Failure_ReturnsFailed()
    {
        var scheduleJobId = Ulid.NewUlid();
        DbContext.ScheduleJob.Add(CreateScheduleJob(scheduleJobId));
        await DbContext.SaveChangesAsync();

        var logger = new Mock<ILogger<RecordingLobLogic>>().Object;
        var orchestrator = CreateOrchestrator(transcodeResult: false);
        var notificationLobLogic = CreateNotificationLobLogic();

        var logic = new RecordingLobLogic(
            logger,
            orchestrator,
            DbContext,
            notificationLobLogic,
            CreateAppConfigurationService(),
            new TagLobLogic(new Mock<ILogger<TagLobLogic>>().Object, DbContext));

        var (isSuccess, error) = await logic.RecordRadioAsync(
            serviceKind: RadioServiceKind.Radiko,
            programId: "P1",
            programName: "Test",
            scheduleJobId: scheduleJobId.ToString(),
            isTimeFree: false,
            startDelay: 0,
            endDelay: 0);

        Assert.That(isSuccess, Is.False);
        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public async Task RecordRadioAsync_TimeFreeFailure_お知らせに失敗詳細が登録される()
    {
        var scheduleJobId = Ulid.NewUlid();
        DbContext.ScheduleJob.Add(CreateScheduleJob(scheduleJobId));
        await DbContext.SaveChangesAsync();

        var logic = new RecordingLobLogic(
            new Mock<ILogger<RecordingLobLogic>>().Object,
            CreateDbOrchestrator(transcodeResult: false),
            DbContext,
            CreateDbNotificationLobLogic(),
            CreateAppConfigurationService(),
            new TagLobLogic(new Mock<ILogger<TagLobLogic>>().Object, DbContext));

        var (isSuccess, error) = await logic.RecordRadioAsync(
            serviceKind: RadioServiceKind.Radiko,
            programId: "P1",
            programName: "Test",
            scheduleJobId: scheduleJobId.ToString(),
            isTimeFree: true,
            startDelay: 0,
            endDelay: 0);

        Assert.That(isSuccess, Is.False);
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Message, Is.EqualTo("タイムフリー録音チャンク取得に失敗しました。"));

        var notification = await DbContext.Notification
            .OrderByDescending(x => x.Timestamp)
            .FirstOrDefaultAsync();

        Assert.That(notification, Is.Not.Null);
        Assert.That(notification!.Category, Is.EqualTo(NoticeCategory.RecordingError));
        Assert.That(notification.Message, Is.EqualTo("Test の録音に失敗しました。理由: タイムフリー録音チャンク取得に失敗しました。"));
    }

    /// <summary>
    /// 異常系: 録音失敗時はスケジュールが残る
    /// </summary>
    [Test]
    public async Task RecordRadioAsync_Failure_スケジュール残る()
    {
        var scheduleJobId = Ulid.NewUlid();
        DbContext.ScheduleJob.Add(CreateScheduleJob(scheduleJobId));
        await DbContext.SaveChangesAsync();

        var logger = new Mock<ILogger<RecordingLobLogic>>().Object;
        var orchestrator = CreateOrchestrator(transcodeResult: false);
        var notificationLobLogic = CreateNotificationLobLogic();

        var logic = new RecordingLobLogic(
            logger,
            orchestrator,
            DbContext,
            notificationLobLogic,
            CreateAppConfigurationService(),
            new TagLobLogic(new Mock<ILogger<TagLobLogic>>().Object, DbContext));

        var (isSuccess, _) = await logic.RecordRadioAsync(
            serviceKind: RadioServiceKind.Radiko,
            programId: "P1",
            programName: "Test",
            scheduleJobId: scheduleJobId.ToString(),
            isTimeFree: false,
            startDelay: 0,
            endDelay: 0);

        Assert.That(isSuccess, Is.False);

        var exists = await DbContext.ScheduleJob.FindAsync(scheduleJobId);
        Assert.That(exists, Is.Not.Null);
    }

    [Test]
    public async Task RecordRadioAsync_キーワード予約タグが録音へ自動付与される()
    {
        var tagId = Guid.NewGuid();
        await DbContext.RecordingTags.AddAsync(new RecordingTag
        {
            Id = tagId,
            Name = "AutoTag",
            NormalizedName = "autotag",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var reserveId = Ulid.NewUlid();
        await DbContext.KeywordReserve.AddAsync(new KeywordReserve
        {
            Id = reserveId,
            Keyword = "test",
            ExcludedKeyword = string.Empty,
            IsTitleOnly = false,
            IsExcludeTitleOnly = false,
            FileName = string.Empty,
            FolderPath = string.Empty,
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            IsEnable = true,
            DaysOfWeek = DaysOfWeek.Monday
        });

        await DbContext.KeywordReserveTagRelations.AddAsync(new KeywordReserveTagRelation
        {
            ReserveId = reserveId,
            TagId = tagId
        });

        var scheduleJobId = Ulid.NewUlid();
        var schedule = CreateScheduleJob(scheduleJobId);
        schedule.KeywordReserveId = reserveId;
        DbContext.ScheduleJob.Add(schedule);
        await DbContext.SaveChangesAsync();

        var logger = new Mock<ILogger<RecordingLobLogic>>().Object;
        var orchestrator = CreateDbOrchestrator(transcodeResult: true);
        var notificationLobLogic = CreateNotificationLobLogic();

        var logic = new RecordingLobLogic(
            logger,
            orchestrator,
            DbContext,
            notificationLobLogic,
            CreateAppConfigurationService(),
            new TagLobLogic(new Mock<ILogger<TagLobLogic>>().Object, DbContext));

        var (isSuccess, error) = await logic.RecordRadioAsync(
            serviceKind: RadioServiceKind.Radiko,
            programId: "P1",
            programName: "Test",
            scheduleJobId: scheduleJobId.ToString(),
            isTimeFree: false,
            startDelay: 0,
            endDelay: 0);

        Assert.That(isSuccess, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(await DbContext.RecordingTagRelations.AnyAsync(r => r.TagId == tagId), Is.True);
    }

    [Test]
    public async Task RecordRadioAsync_複数キーワード予約のタグをマージして録音へ付与できる()
    {
        var tagId1 = Guid.NewGuid();
        var tagId2 = Guid.NewGuid();

        await DbContext.RecordingTags.AddRangeAsync([
            new RecordingTag
            {
                Id = tagId1,
                Name = "Tag1",
                NormalizedName = $"tag1-{tagId1:N}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new RecordingTag
            {
                Id = tagId2,
                Name = "Tag2",
                NormalizedName = $"tag2-{tagId2:N}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        ]);

        var reserveId1 = Ulid.NewUlid();
        var reserveId2 = Ulid.NewUlid();

        await DbContext.KeywordReserve.AddRangeAsync([
            new KeywordReserve
            {
                Id = reserveId1,
                Keyword = "keyword1",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = string.Empty,
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Monday
            },
            new KeywordReserve
            {
                Id = reserveId2,
                Keyword = "keyword2",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = string.Empty,
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Monday
            }
        ]);

        await DbContext.KeywordReserveTagRelations.AddRangeAsync([
            new KeywordReserveTagRelation
            {
                ReserveId = reserveId1,
                TagId = tagId1
            },
            new KeywordReserveTagRelation
            {
                ReserveId = reserveId2,
                TagId = tagId2
            }
        ]);

        var scheduleJobId = Ulid.NewUlid();
        var schedule = CreateScheduleJob(scheduleJobId);
        schedule.KeywordReserveId = reserveId1;
        DbContext.ScheduleJob.Add(schedule);
        await DbContext.ScheduleJobKeywordReserveRelations.AddRangeAsync([
            new ScheduleJobKeywordReserveRelation
            {
                ScheduleJobId = scheduleJobId,
                KeywordReserveId = reserveId1
            },
            new ScheduleJobKeywordReserveRelation
            {
                ScheduleJobId = scheduleJobId,
                KeywordReserveId = reserveId2
            }
        ]);

        await DbContext.SaveChangesAsync();

        var logger = new Mock<ILogger<RecordingLobLogic>>().Object;
        var orchestrator = CreateDbOrchestrator(transcodeResult: true);
        var notificationLobLogic = CreateNotificationLobLogic();

        var logic = new RecordingLobLogic(
            logger,
            orchestrator,
            DbContext,
            notificationLobLogic,
            CreateAppConfigurationService(mergeTagsFromAllMatchedKeywordRules: true),
            new TagLobLogic(new Mock<ILogger<TagLobLogic>>().Object, DbContext));

        var (isSuccess, error) = await logic.RecordRadioAsync(
            serviceKind: RadioServiceKind.Radiko,
            programId: "P1",
            programName: "Test",
            scheduleJobId: scheduleJobId.ToString(),
            isTimeFree: false,
            startDelay: 0,
            endDelay: 0);

        Assert.That(isSuccess, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(await DbContext.RecordingTagRelations.AnyAsync(r => r.TagId == tagId1), Is.True);
        Assert.That(await DbContext.RecordingTagRelations.AnyAsync(r => r.TagId == tagId2), Is.True);
    }

    [Test]
    public async Task RecordRadioAsync_個別設定ForceSingleは全体設定より優先される()
    {
        var tagId1 = Guid.NewGuid();
        var tagId2 = Guid.NewGuid();

        await DbContext.RecordingTags.AddRangeAsync([
            new RecordingTag
            {
                Id = tagId1,
                Name = "Tag1",
                NormalizedName = $"tag1-{tagId1:N}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new RecordingTag
            {
                Id = tagId2,
                Name = "Tag2",
                NormalizedName = $"tag2-{tagId2:N}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        ]);

        var reserveId1 = Ulid.NewUlid();
        var reserveId2 = Ulid.NewUlid();

        await DbContext.KeywordReserve.AddRangeAsync([
            new KeywordReserve
            {
                Id = reserveId1,
                Keyword = "keyword1",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = string.Empty,
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Monday,
                MergeTagBehavior = KeywordReserveTagMergeBehavior.ForceSingle
            },
            new KeywordReserve
            {
                Id = reserveId2,
                Keyword = "keyword2",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = string.Empty,
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Monday,
                MergeTagBehavior = KeywordReserveTagMergeBehavior.Default
            }
        ]);

        await DbContext.KeywordReserveTagRelations.AddRangeAsync([
            new KeywordReserveTagRelation { ReserveId = reserveId1, TagId = tagId1 },
            new KeywordReserveTagRelation { ReserveId = reserveId2, TagId = tagId2 }
        ]);

        var scheduleJobId = Ulid.NewUlid();
        var schedule = CreateScheduleJob(scheduleJobId);
        schedule.KeywordReserveId = reserveId1;
        DbContext.ScheduleJob.Add(schedule);
        await DbContext.ScheduleJobKeywordReserveRelations.AddRangeAsync([
            new ScheduleJobKeywordReserveRelation { ScheduleJobId = scheduleJobId, KeywordReserveId = reserveId1 },
            new ScheduleJobKeywordReserveRelation { ScheduleJobId = scheduleJobId, KeywordReserveId = reserveId2 }
        ]);
        await DbContext.SaveChangesAsync();

        var logic = new RecordingLobLogic(
            new Mock<ILogger<RecordingLobLogic>>().Object,
            CreateDbOrchestrator(transcodeResult: true),
            DbContext,
            CreateNotificationLobLogic(),
            CreateAppConfigurationService(mergeTagsFromAllMatchedKeywordRules: true),
            new TagLobLogic(new Mock<ILogger<TagLobLogic>>().Object, DbContext));

        var (isSuccess, error) = await logic.RecordRadioAsync(
            serviceKind: RadioServiceKind.Radiko,
            programId: "P1",
            programName: "Test",
            scheduleJobId: scheduleJobId.ToString(),
            isTimeFree: false,
            startDelay: 0,
            endDelay: 0);

        Assert.That(isSuccess, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(await DbContext.RecordingTagRelations.AnyAsync(r => r.TagId == tagId1), Is.False);
        Assert.That(await DbContext.RecordingTagRelations.AnyAsync(r => r.TagId == tagId2), Is.True);
    }

    [Test]
    public async Task RecordRadioAsync_個別設定ForceMergeは全体設定より優先される()
    {
        var tagId1 = Guid.NewGuid();
        var tagId2 = Guid.NewGuid();

        await DbContext.RecordingTags.AddRangeAsync([
            new RecordingTag
            {
                Id = tagId1,
                Name = "Tag1",
                NormalizedName = $"tag1-{tagId1:N}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new RecordingTag
            {
                Id = tagId2,
                Name = "Tag2",
                NormalizedName = $"tag2-{tagId2:N}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        ]);

        var reserveId1 = Ulid.NewUlid();
        var reserveId2 = Ulid.NewUlid();

        await DbContext.KeywordReserve.AddRangeAsync([
            new KeywordReserve
            {
                Id = reserveId1,
                Keyword = "keyword1",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = string.Empty,
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Monday,
                MergeTagBehavior = KeywordReserveTagMergeBehavior.ForceMerge
            },
            new KeywordReserve
            {
                Id = reserveId2,
                Keyword = "keyword2",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = string.Empty,
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Monday,
                MergeTagBehavior = KeywordReserveTagMergeBehavior.Default
            }
        ]);

        await DbContext.KeywordReserveTagRelations.AddRangeAsync([
            new KeywordReserveTagRelation { ReserveId = reserveId1, TagId = tagId1 },
            new KeywordReserveTagRelation { ReserveId = reserveId2, TagId = tagId2 }
        ]);

        var scheduleJobId = Ulid.NewUlid();
        var schedule = CreateScheduleJob(scheduleJobId);
        schedule.KeywordReserveId = reserveId1;
        DbContext.ScheduleJob.Add(schedule);
        await DbContext.ScheduleJobKeywordReserveRelations.AddRangeAsync([
            new ScheduleJobKeywordReserveRelation { ScheduleJobId = scheduleJobId, KeywordReserveId = reserveId1 },
            new ScheduleJobKeywordReserveRelation { ScheduleJobId = scheduleJobId, KeywordReserveId = reserveId2 }
        ]);
        await DbContext.SaveChangesAsync();

        var logic = new RecordingLobLogic(
            new Mock<ILogger<RecordingLobLogic>>().Object,
            CreateDbOrchestrator(transcodeResult: true),
            DbContext,
            CreateNotificationLobLogic(),
            CreateAppConfigurationService(mergeTagsFromAllMatchedKeywordRules: false),
            new TagLobLogic(new Mock<ILogger<TagLobLogic>>().Object, DbContext));

        var (isSuccess, error) = await logic.RecordRadioAsync(
            serviceKind: RadioServiceKind.Radiko,
            programId: "P1",
            programName: "Test",
            scheduleJobId: scheduleJobId.ToString(),
            isTimeFree: false,
            startDelay: 0,
            endDelay: 0);

        Assert.That(isSuccess, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(await DbContext.RecordingTagRelations.AnyAsync(r => r.TagId == tagId1), Is.True);
        Assert.That(await DbContext.RecordingTagRelations.AnyAsync(r => r.TagId == tagId2), Is.False);
    }

    [Test]
    public async Task RecordRadioAsync_複数一致時はForceSingleルールのみ除外される()
    {
        var tagId1 = Guid.NewGuid();
        var tagId2 = Guid.NewGuid();
        var tagId3 = Guid.NewGuid();

        await DbContext.RecordingTags.AddRangeAsync([
            new RecordingTag
            {
                Id = tagId1,
                Name = "Tag1",
                NormalizedName = $"tag1-{tagId1:N}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new RecordingTag
            {
                Id = tagId2,
                Name = "Tag2",
                NormalizedName = $"tag2-{tagId2:N}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new RecordingTag
            {
                Id = tagId3,
                Name = "Tag3",
                NormalizedName = $"tag3-{tagId3:N}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        ]);

        var reserveId1 = Ulid.NewUlid();
        var reserveId2 = Ulid.NewUlid();
        var reserveId3 = Ulid.NewUlid();

        await DbContext.KeywordReserve.AddRangeAsync([
            new KeywordReserve
            {
                Id = reserveId1,
                Keyword = "keyword1",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = string.Empty,
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Monday,
                SortOrder = 1,
                MergeTagBehavior = KeywordReserveTagMergeBehavior.Default
            },
            new KeywordReserve
            {
                Id = reserveId2,
                Keyword = "keyword2",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = string.Empty,
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Monday,
                SortOrder = 2,
                MergeTagBehavior = KeywordReserveTagMergeBehavior.Default
            },
            new KeywordReserve
            {
                Id = reserveId3,
                Keyword = "keyword3",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = string.Empty,
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Monday,
                SortOrder = 3,
                MergeTagBehavior = KeywordReserveTagMergeBehavior.ForceSingle
            }
        ]);

        await DbContext.KeywordReserveTagRelations.AddRangeAsync([
            new KeywordReserveTagRelation { ReserveId = reserveId1, TagId = tagId1 },
            new KeywordReserveTagRelation { ReserveId = reserveId2, TagId = tagId2 },
            new KeywordReserveTagRelation { ReserveId = reserveId3, TagId = tagId3 }
        ]);

        var scheduleJobId = Ulid.NewUlid();
        var schedule = CreateScheduleJob(scheduleJobId);
        schedule.KeywordReserveId = reserveId1;
        schedule.ReserveType = ReserveType.Keyword;
        DbContext.ScheduleJob.Add(schedule);
        await DbContext.ScheduleJobKeywordReserveRelations.AddRangeAsync([
            new ScheduleJobKeywordReserveRelation { ScheduleJobId = scheduleJobId, KeywordReserveId = reserveId2 },
            new ScheduleJobKeywordReserveRelation { ScheduleJobId = scheduleJobId, KeywordReserveId = reserveId3 }
        ]);
        await DbContext.SaveChangesAsync();

        var logic = new RecordingLobLogic(
            new Mock<ILogger<RecordingLobLogic>>().Object,
            CreateDbOrchestrator(transcodeResult: true),
            DbContext,
            CreateNotificationLobLogic(),
            CreateAppConfigurationService(mergeTagsFromAllMatchedKeywordRules: true),
            new TagLobLogic(new Mock<ILogger<TagLobLogic>>().Object, DbContext));

        var (isSuccess, error) = await logic.RecordRadioAsync(
            serviceKind: RadioServiceKind.Radiko,
            programId: "P1",
            programName: "Test",
            scheduleJobId: scheduleJobId.ToString(),
            isTimeFree: false,
            startDelay: 0,
            endDelay: 0);

        Assert.That(isSuccess, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(await DbContext.RecordingTagRelations.AnyAsync(r => r.TagId == tagId1), Is.True);
        Assert.That(await DbContext.RecordingTagRelations.AnyAsync(r => r.TagId == tagId2), Is.True);
        Assert.That(await DbContext.RecordingTagRelations.AnyAsync(r => r.TagId == tagId3), Is.False);
    }

    [Test]
    public async Task RecordRadioAsync_同一ProgramIdの別Schedule関連からもタグ統合できる()
    {
        var tagId1 = Guid.NewGuid();
        var tagId2 = Guid.NewGuid();

        await DbContext.RecordingTags.AddRangeAsync([
            new RecordingTag
            {
                Id = tagId1,
                Name = "Tag1",
                NormalizedName = $"tag1-{tagId1:N}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new RecordingTag
            {
                Id = tagId2,
                Name = "Tag2",
                NormalizedName = $"tag2-{tagId2:N}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        ]);

        var reserveId1 = Ulid.NewUlid();
        var reserveId2 = Ulid.NewUlid();
        var sameProgramId = "P1";

        await DbContext.KeywordReserve.AddRangeAsync([
            new KeywordReserve
            {
                Id = reserveId1,
                Keyword = "keyword1",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = string.Empty,
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Monday
            },
            new KeywordReserve
            {
                Id = reserveId2,
                Keyword = "keyword2",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = string.Empty,
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Monday
            }
        ]);

        await DbContext.KeywordReserveTagRelations.AddRangeAsync([
            new KeywordReserveTagRelation { ReserveId = reserveId1, TagId = tagId1 },
            new KeywordReserveTagRelation { ReserveId = reserveId2, TagId = tagId2 }
        ]);

        var executingScheduleJobId = Ulid.NewUlid();
        var executingSchedule = CreateScheduleJob(executingScheduleJobId);
        executingSchedule.ProgramId = sameProgramId;
        executingSchedule.KeywordReserveId = reserveId1;
        executingSchedule.ReserveType = ReserveType.Keyword;

        var siblingScheduleJobId = Ulid.NewUlid();
        var siblingSchedule = CreateScheduleJob(siblingScheduleJobId);
        siblingSchedule.ProgramId = sameProgramId;
        siblingSchedule.KeywordReserveId = reserveId2;
        siblingSchedule.ReserveType = ReserveType.Keyword;

        DbContext.ScheduleJob.AddRange(executingSchedule, siblingSchedule);

        // 実行対象Scheduleにはreserve1のみ関連がある状態を再現
        await DbContext.ScheduleJobKeywordReserveRelations.AddAsync(
            new ScheduleJobKeywordReserveRelation
            {
                ScheduleJobId = executingScheduleJobId,
                KeywordReserveId = reserveId1
            });

        // 別Schedule側にreserve2関連が存在
        await DbContext.ScheduleJobKeywordReserveRelations.AddAsync(
            new ScheduleJobKeywordReserveRelation
            {
                ScheduleJobId = siblingScheduleJobId,
                KeywordReserveId = reserveId2
            });

        await DbContext.SaveChangesAsync();

        var logic = new RecordingLobLogic(
            new Mock<ILogger<RecordingLobLogic>>().Object,
            CreateDbOrchestrator(transcodeResult: true),
            DbContext,
            CreateNotificationLobLogic(),
            CreateAppConfigurationService(mergeTagsFromAllMatchedKeywordRules: true),
            new TagLobLogic(new Mock<ILogger<TagLobLogic>>().Object, DbContext));

        var (isSuccess, error) = await logic.RecordRadioAsync(
            serviceKind: RadioServiceKind.Radiko,
            programId: sameProgramId,
            programName: "Test",
            scheduleJobId: executingScheduleJobId.ToString(),
            isTimeFree: false,
            startDelay: 0,
            endDelay: 0);

        Assert.That(isSuccess, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(await DbContext.RecordingTagRelations.AnyAsync(r => r.TagId == tagId1), Is.True);
        Assert.That(await DbContext.RecordingTagRelations.AnyAsync(r => r.TagId == tagId2), Is.True);
    }
}
