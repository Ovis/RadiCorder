using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RadiCorder.Logics.Context;
using RadiCorder.Logics.Domain.ProgramSchedule;
using RadiCorder.Logics.Domain.Notification;
using RadiCorder.Logics.Domain.Reserve;
using RadiCorder.Logics.Infrastructure.Notification;
using RadiCorder.Logics.Infrastructure.ProgramSchedule;
using RadiCorder.Logics.Infrastructure.Reserve;
using RadiCorder.Logics.Interfaces;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Logics.RadikoLogic;
using RadiCorder.Logics.Logics.RecordJobLogic;
using RadiCorder.Logics.Logics.ReserveLogic;
using RadiCorder.Logics.Logics.TagLogic;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Models.Radiko;
using RadiCorder.Logics.Primitives.DataAnnotations;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.Tests.Mocks;

namespace RadiCorder.Logics.Tests.LogicTest
{
    [TestFixture]
    public class ReserveLobLogicTests : UnitTestBase
    {
        private RadioDbContext _dbContext;
        private ReserveLobLogic _reserveLobLogic;
        private Mock<IAppConfigurationService> _configServiceMock;
        private IEntryMapper _entryMapper;
        private Mock<ILogger<ReserveLobLogic>> _loggerMock;
        private Mock<IRadioAppContext> _appContextMock;
        private Mock<RadikoUniqueProcessLogic> _radikoUniqueProcessLogicMock;
        private RecordJobLobLogic _recordJobLobLogic = null!;
        private Mock<NotificationLobLogic> _notificationLobLogicMock;
        private Mock<ProgramScheduleLobLogic> _programScheduleLobLogicMock;

        [SetUp]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<ReserveLobLogic>>();
            _configServiceMock = new Mock<IAppConfigurationService>();
            _appContextMock = new Mock<IRadioAppContext>();
            _dbContext = DbContext;

            _appContextMock.SetupGet(x => x.StandardDateTimeOffset).Returns(DateTimeOffset.Now);
            _entryMapper = new EntryMapper(_configServiceMock.Object);

            // 放送局名解決のための辞書を用意
            var stations = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
            stations["TBS"] = "TBS";
            stations["CBC"] = "CBC";
            _configServiceMock.SetupGet(x => x.RadikoStationDic).Returns(stations);
            _configServiceMock.SetupGet(x => x.RadiruArea).Returns("130");

            _recordJobLobLogic = new RecordJobLobLogic(
                new Mock<ILogger<RecordJobLobLogic>>().Object,
                _configServiceMock.Object);

            _notificationLobLogicMock = new Mock<NotificationLobLogic>(
                new Mock<ILogger<NotificationLobLogic>>().Object,
                _appContextMock.Object,
                _configServiceMock.Object,
                _entryMapper,
                new FakeHttpClientFactory(new HttpClient(new FakeHttpMessageHandler())),
                new NotificationRepository(DbContext),
                Mock.Of<INotificationEventPublisher>()
            );

            _radikoUniqueProcessLogicMock = new Mock<RadikoUniqueProcessLogic>(
                new Mock<ILogger<RadikoUniqueProcessLogic>>().Object,
                _configServiceMock.Object,
                new FakeHttpClientFactory(new HttpClient(new FakeHttpMessageHandler()))
            );

            var radikoApiClientMock = new Mock<IRadikoApiClient>();
            var radiruApiClientMock = new Mock<IRadiruApiClient>();
            var programScheduleRepository = new ProgramScheduleRepository(DbContext);
            var reserveRepository = new ReserveRepository(DbContext);

            _programScheduleLobLogicMock = new Mock<ProgramScheduleLobLogic>(
                new Mock<ILogger<ProgramScheduleLobLogic>>().Object,
                _appContextMock.Object,
                radikoApiClientMock.Object,
                radiruApiClientMock.Object,
                programScheduleRepository,
                _recordJobLobLogic,
                _entryMapper,
                _notificationLobLogicMock.Object
            );

            _reserveLobLogic = new ReserveLobLogic(
                _loggerMock.Object,
                _appContextMock.Object,
                _configServiceMock.Object,
                reserveRepository,
                programScheduleRepository,
                _recordJobLobLogic,
                _programScheduleLobLogicMock.Object,
                _notificationLobLogicMock.Object,
                new TagLobLogic(new Mock<ILogger<TagLobLogic>>().Object, DbContext),
                _entryMapper);
        }


        [Test]
        public async Task SetRecordingJobByProgramIdAsync_radiko番組録音予約テスト()
        {
            {
                var programEntry = new RadikoProgram
                {
                    ProgramId = "TBS_2124070100000020240701003000",
                    Title = "林原めぐみのTokyo Boogie Night",
                    StartTime = new DateTimeOffset(2124, 7, 1, 0, 0, 0, TimeSpan.FromHours(9)).UtcDateTime,
                    EndTime = new DateTimeOffset(2124, 7, 1, 0, 30, 0, TimeSpan.FromHours(9)).UtcDateTime,
                    StationId = "TBS",
                    RadioDate = DateOnly.Parse("2124-06-30"),
                    DaysOfWeek = DaysOfWeek.Sunday,
                    AvailabilityTimeFree = AvailabilityTimeFree.Available
                };

                var dbTran = await _dbContext.Database.BeginTransactionAsync();
                try
                {
                    _dbContext.RadikoPrograms.Add(programEntry);
                    await _dbContext.SaveChangesAsync();

                    await dbTran.CommitAsync();
                }
                catch (Exception e)
                {
                    await dbTran.RollbackAsync();
                    Assert.Fail(e.Message);
                }
            }

            var result = await _reserveLobLogic.SetRecordingJobByProgramIdAsync(
                "TBS_2124070100000020240701003000",
                RadioServiceKind.Radiko,
                RecordingType.TimeFree);

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.Null);
        }


        [Test]
        public async Task SetRecordingJobByProgramIdAsync_radiko存在しない番組指定テスト()
        {
            // Arrange
            var serviceKind = RadioServiceKind.Radiko;
            var type = RecordingType.TimeFree;

            // Act
            var result = await _reserveLobLogic.SetRecordingJobByProgramIdAsync(
                "TBS_1234567890123456789012345678", // 存在しないID指定
                serviceKind,
                type);

            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Is.Not.Null);
            Assert.That(result.Error?.Message, Is.EqualTo("指定された番組が番組表にありませんでした。"));
        }


        [Test]
        public async Task SetRecordingJobByProgramIdAsync_未対応サービステスト()
        {
            var result = await _reserveLobLogic.SetRecordingJobByProgramIdAsync(
                "TBS_2124070100000020240701003000",
                RadioServiceKind.Other,
                RecordingType.TimeFree);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Is.Not.Null);
            Assert.That(result.Error?.Message, Is.EqualTo("未対応のサービスです。"));
        }

        [Test]
        public async Task SetRecordingJobByProgramIdAsync_radiru番組録音予約テスト()
        {
            var dayOfWeek = (DaysOfWeek)_appContextMock.Object.StandardDateTimeOffset.DayOfWeek;
            var date = DateOnly.FromDateTime(_appContextMock.Object.StandardDateTimeOffset.UtcDateTime.Date);
            var areaTokyo = RadiCorder.Logics.Models.NhkRadiru.RadiruAreaKind.東京.GetEnumCodeId();

            var programEntry = new NhkRadiruProgram
            {
                ProgramId = "R1_21240701000000",
                Title = "NHK R1",
                StationId = "r1",
                AreaId = areaTokyo,
                RadioDate = date,
                DaysOfWeek = dayOfWeek,
                StartTime = _appContextMock.Object.StandardDateTimeOffset.AddHours(1),
                EndTime = _appContextMock.Object.StandardDateTimeOffset.AddHours(1).AddMinutes(30),
                EventId = "EV1",
                SiteId = "site",
                ProgramUrl = "http://example"
            };

            await using var dbTran = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                _dbContext.NhkRadiruPrograms.Add(programEntry);
                await _dbContext.SaveChangesAsync();

                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            var result = await _reserveLobLogic.SetRecordingJobByProgramIdAsync(
                "R1_21240701000000",
                RadioServiceKind.Radiru,
                RecordingType.RealTime);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.Null);
        }

        [Test]
        public async Task SetRecordingJobByProgramIdAsync_放送終了後はタイムフリーとしてDB登録される()
        {
            var now = _appContextMock.Object.StandardDateTimeOffset;
            var dayOfWeek = (DaysOfWeek)now.DayOfWeek;
            var beforeCount = _dbContext.ScheduleJob.Count();

            var programEntry = new RadikoProgram
            {
                ProgramId = "TBS_OLD_001",
                Title = "Past Program",
                StartTime = now.AddMinutes(-60),
                EndTime = now.AddMinutes(-30),
                StationId = "TBS",
                RadioDate = DateOnly.FromDateTime(now.UtcDateTime.Date),
                DaysOfWeek = dayOfWeek,
                AvailabilityTimeFree = AvailabilityTimeFree.Available
            };

            await using var dbTran = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                _dbContext.RadikoPrograms.Add(programEntry);
                await _dbContext.SaveChangesAsync();

                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            var result = await _reserveLobLogic.SetRecordingJobByProgramIdAsync(
                "TBS_OLD_001",
                RadioServiceKind.Radiko,
                RecordingType.RealTime);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.Null);

            var count = _dbContext.ScheduleJob.Count();
            Assert.That(count, Is.EqualTo(beforeCount + 1));
        }

        [Test]
        public async Task GetReserveListAsync_予約一覧取得テスト()
        {
            // Arrange
            var scheduleJobs = new List<ScheduleJob>
            {
                new()
                {
                    Id = Ulid.NewUlid(),
                    ServiceKind = RadioServiceKind.Radiko,
                    StationId = "TBS",
                    ProgramId = "TBS_1234567890123456789012345678",
                    FilePath = "",
                    StartDateTime = DateTime.Today.AddDays(3),
                    EndDateTime = DateTime.Today.AddDays(4),
                    Title = "TestTitle",
                    RecordingType = RecordingType.TimeFree,
                    ReserveType = ReserveType.Program,
                    IsEnabled = true
                },
                new()
                {
                    Id = Ulid.NewUlid(),
                    ServiceKind = RadioServiceKind.Radiko,
                    StationId = "CBC",
                    ProgramId = "CBC_9876543210123456789012345678",
                    FilePath = "",
                    StartDateTime = DateTime.Today.AddDays(4),
                    EndDateTime = DateTime.Today.AddDays(5),
                    Title = "TestTitle2",
                    RecordingType = RecordingType.TimeFree,
                    ReserveType = ReserveType.Program,
                    IsEnabled = true
                }
            };

            await using var dbTran = await DbContext.Database.BeginTransactionAsync();
            try
            {
                DbContext.ScheduleJob.AddRange(scheduleJobs);
                await DbContext.SaveChangesAsync();

                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            // Act
            var result = await _reserveLobLogic.GetReserveListAsync();

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Entry?.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(result.Error, Is.Null);
            var first = result.Entry!.Single(x => x.ProgramId == "TBS_1234567890123456789012345678");
            Assert.That(first.MatchedKeywordReserveKeywords, Is.Empty);
            Assert.That(first.PlannedTagNames, Is.Empty);
        }

        [Test]
        public async Task GetReserveListAsync_キーワード予約由来情報と付与予定タグが取得できる()
        {
            _configServiceMock.SetupGet(x => x.MergeTagsFromAllMatchedKeywordRules).Returns(true);

            var scheduleJobId = Ulid.NewUlid();
            var reserveIdA = Ulid.NewUlid();
            var reserveIdB = Ulid.NewUlid();
            var tagA = new RecordingTag
            {
                Id = Guid.NewGuid(),
                Name = "タグA",
                NormalizedName = "タグA",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var tagB = new RecordingTag
            {
                Id = Guid.NewGuid(),
                Name = "タグB",
                NormalizedName = "タグB",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var scheduleJob = new ScheduleJob
            {
                Id = scheduleJobId,
                ServiceKind = RadioServiceKind.Radiko,
                StationId = "TBS",
                ProgramId = "TBS_TEST_001",
                FilePath = "",
                StartDateTime = DateTime.Today.AddDays(1),
                EndDateTime = DateTime.Today.AddDays(1).AddMinutes(30),
                Title = "Keyword Match Program",
                RecordingType = RecordingType.TimeFree,
                ReserveType = ReserveType.Keyword,
                IsEnabled = true,
                KeywordReserveId = reserveIdA
            };

            var keywordReserveA = new KeywordReserve
            {
                Id = reserveIdA,
                Keyword = "キーワードA",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = string.Empty,
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Sunday | DaysOfWeek.Monday | DaysOfWeek.Tuesday |
                             DaysOfWeek.Wednesday | DaysOfWeek.Thursday | DaysOfWeek.Friday | DaysOfWeek.Saturday,
                SortOrder = 2
            };

            var keywordReserveB = new KeywordReserve
            {
                Id = reserveIdB,
                Keyword = "キーワードB",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = string.Empty,
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Sunday | DaysOfWeek.Monday | DaysOfWeek.Tuesday |
                             DaysOfWeek.Wednesday | DaysOfWeek.Thursday | DaysOfWeek.Friday | DaysOfWeek.Saturday,
                SortOrder = 1
            };

            await using var dbTran = await DbContext.Database.BeginTransactionAsync();
            try
            {
                DbContext.ScheduleJob.Add(scheduleJob);
                DbContext.KeywordReserve.AddRange(keywordReserveA, keywordReserveB);
                DbContext.ScheduleJobKeywordReserveRelations.Add(new ScheduleJobKeywordReserveRelation
                {
                    ScheduleJobId = scheduleJobId,
                    KeywordReserveId = reserveIdB
                });
                DbContext.RecordingTags.AddRange(tagA, tagB);
                DbContext.KeywordReserveTagRelations.AddRange(
                    new KeywordReserveTagRelation { ReserveId = reserveIdA, TagId = tagA.Id },
                    new KeywordReserveTagRelation { ReserveId = reserveIdB, TagId = tagB.Id });
                await DbContext.SaveChangesAsync();
                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            var result = await _reserveLobLogic.GetReserveListAsync();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Entry, Is.Not.Null);

            var entry = result.Entry!.Single(x => x.Id == scheduleJobId);
            Assert.That(entry.MatchedKeywordReserveKeywords, Is.EqualTo(new[] { "キーワードB", "キーワードA" }));
            Assert.That(entry.PlannedTagNames, Is.EqualTo(new[] { "タグA", "タグB" }));
        }

        [Test]
        public async Task GetReserveListAsync_ForceSingleルールのタグは他ルール一致時に付与予定から除外される()
        {
            _configServiceMock.SetupGet(x => x.MergeTagsFromAllMatchedKeywordRules).Returns(true);

            var scheduleJobId = Ulid.NewUlid();
            var reserveIdA = Ulid.NewUlid();
            var reserveIdB = Ulid.NewUlid();
            var reserveIdC = Ulid.NewUlid();

            var tag1 = new RecordingTag
            {
                Id = Guid.NewGuid(),
                Name = "タグ1",
                NormalizedName = "タグ1",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var tag2 = new RecordingTag
            {
                Id = Guid.NewGuid(),
                Name = "タグ2",
                NormalizedName = "タグ2",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var tag3 = new RecordingTag
            {
                Id = Guid.NewGuid(),
                Name = "タグ3",
                NormalizedName = "タグ3",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var scheduleJob = new ScheduleJob
            {
                Id = scheduleJobId,
                ServiceKind = RadioServiceKind.Radiko,
                StationId = "TBS",
                ProgramId = "TBS_TEST_002",
                FilePath = "",
                StartDateTime = DateTime.Today.AddDays(1),
                EndDateTime = DateTime.Today.AddDays(1).AddMinutes(30),
                Title = "Keyword Match Program 2",
                RecordingType = RecordingType.TimeFree,
                ReserveType = ReserveType.Keyword,
                IsEnabled = true,
                KeywordReserveId = reserveIdA
            };

            var reserveA = new KeywordReserve
            {
                Id = reserveIdA,
                Keyword = "キーワードA",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = string.Empty,
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Sunday,
                SortOrder = 1,
                MergeTagBehavior = KeywordReserveTagMergeBehavior.Default
            };
            var reserveB = new KeywordReserve
            {
                Id = reserveIdB,
                Keyword = "キーワードB",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = string.Empty,
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Sunday,
                SortOrder = 2,
                MergeTagBehavior = KeywordReserveTagMergeBehavior.Default
            };
            var reserveC = new KeywordReserve
            {
                Id = reserveIdC,
                Keyword = "キーワードC",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = string.Empty,
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Sunday,
                SortOrder = 3,
                MergeTagBehavior = KeywordReserveTagMergeBehavior.ForceSingle
            };

            await using var dbTran = await DbContext.Database.BeginTransactionAsync();
            try
            {
                DbContext.ScheduleJob.Add(scheduleJob);
                DbContext.KeywordReserve.AddRange(reserveA, reserveB, reserveC);
                DbContext.ScheduleJobKeywordReserveRelations.AddRange(
                    new ScheduleJobKeywordReserveRelation { ScheduleJobId = scheduleJobId, KeywordReserveId = reserveIdB },
                    new ScheduleJobKeywordReserveRelation { ScheduleJobId = scheduleJobId, KeywordReserveId = reserveIdC });
                DbContext.RecordingTags.AddRange(tag1, tag2, tag3);
                DbContext.KeywordReserveTagRelations.AddRange(
                    new KeywordReserveTagRelation { ReserveId = reserveIdA, TagId = tag1.Id },
                    new KeywordReserveTagRelation { ReserveId = reserveIdB, TagId = tag2.Id },
                    new KeywordReserveTagRelation { ReserveId = reserveIdC, TagId = tag3.Id });
                await DbContext.SaveChangesAsync();
                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            var result = await _reserveLobLogic.GetReserveListAsync();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Entry, Is.Not.Null);

            var entry = result.Entry!.Single(x => x.Id == scheduleJobId);
            Assert.That(entry.MatchedKeywordReserveKeywords, Is.EqualTo(new[] { "キーワードA", "キーワードB", "キーワードC" }));
            Assert.That(entry.PlannedTagNames, Is.EqualTo(new[] { "タグ1", "タグ2" }));
        }


        [Test]
        public async Task DeleteProgramReserveEntryAsync_予約削除テスト()
        {
            var scheduleJob = new ScheduleJob
            {
                Id = Ulid.NewUlid(),
                ServiceKind = RadioServiceKind.Radiko,
                StationId = "CBC",
                ProgramId = "CBC_9876543210123456789012345678",
                FilePath = "",
                StartDateTime = DateTime.Today.AddDays(4),
                EndDateTime = DateTime.Today.AddDays(5),
                Title = "TestTitle2",
                RecordingType = RecordingType.TimeFree,
                ReserveType = ReserveType.Program,
                IsEnabled = true
            };

            await using var dbTran = await DbContext.Database.BeginTransactionAsync();
            try
            {
                DbContext.ScheduleJob.Add(scheduleJob);
                await DbContext.SaveChangesAsync();

                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            var result = await _reserveLobLogic.DeleteProgramReserveEntryAsync(scheduleJob.Id);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.Null);
        }

        [Test]
        public async Task DeleteProgramReserveEntryAsync_存在しない予約の削除テスト()
        {
            var jobId = Ulid.NewUlid();

            var result = await _reserveLobLogic.DeleteProgramReserveEntryAsync(jobId);

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.Null);
        }


        [Test]
        public async Task GetKeywordReserveListAsync_WhenCalled_キーワード予約取得テスト()
        {
            var keywordReserve = new KeywordReserve
            {
                Id = Ulid.NewUlid(),
                Keyword = "Keyword Test1",
                ExcludedKeyword = "ExcludedKeyword",
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = "filename.m4a",
                FolderPath = @"Folder\Path1",
                StartTime = new TimeOnly(0, 0, 0),
                EndTime = new TimeOnly(23, 59, 59),
                DaysOfWeek = DaysOfWeek.Sunday | DaysOfWeek.Monday | DaysOfWeek.Tuesday | DaysOfWeek.Wednesday | DaysOfWeek.Thursday | DaysOfWeek.Friday | DaysOfWeek.Thursday,
                IsEnable = true,
                StartDelay = new TimeSpan(30),
                EndDelay = new TimeSpan(30)
            };

            await using var dbTran = await DbContext.Database.BeginTransactionAsync();
            try
            {
                DbContext.KeywordReserve.Add(keywordReserve);
                await DbContext.SaveChangesAsync();

                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            var result = await _reserveLobLogic.GetKeywordReserveListAsync();

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Entry, Is.Not.Null);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Entry?.First().Id, Is.EqualTo(keywordReserve.Id));
        }

        [Test]
        public async ValueTask UpdateKeywordReserveAsync_キーワード予約更新テスト()
        {
            var keywordReserve = new KeywordReserve
            {
                Id = Ulid.NewUlid(),
                Keyword = "Keyword Test1",
                ExcludedKeyword = "ExcludedKeyword",
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = "filename.m4a",
                FolderPath = @"Folder\Path1",
                StartTime = new TimeOnly(0, 0, 0),
                EndTime = new TimeOnly(23, 59, 59),
                DaysOfWeek = DaysOfWeek.Sunday | DaysOfWeek.Monday | DaysOfWeek.Tuesday | DaysOfWeek.Wednesday | DaysOfWeek.Thursday | DaysOfWeek.Friday | DaysOfWeek.Thursday,
                IsEnable = true,
                StartDelay = new TimeSpan(30),
                EndDelay = new TimeSpan(30)
            };

            await using var dbTran = await DbContext.Database.BeginTransactionAsync();
            try
            {
                DbContext.KeywordReserve.Add(keywordReserve);
                await DbContext.SaveChangesAsync();

                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            var updateEntry = new KeywordReserveEntry
            {
                Id = keywordReserve.Id,
                Keyword = "Keyword Test2",
                ExcludedKeyword = "ExcludedKeyword2",
                SearchTitleOnly = true,
                ExcludeTitleOnly = true,
                RecordFileName = "filename2.m4a",
                RecordPath = @"Folder\Path2",
                StartTimeString = new TimeOnly(1, 0, 0).ToLongTimeString(),
                EndTimeString = new TimeOnly(22, 59, 59).ToLongTimeString(),
                SelectedDaysOfWeek = [DaysOfWeek.Wednesday],
                IsEnabled = false,
                StartDelay = 60,
                EndDelay = 60
            };

            // Act
            var result = await _reserveLobLogic.UpdateKeywordReserveAsync(updateEntry);

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.Null);
        }

        [Test]
        public async ValueTask UpdateKeywordReserveAsync_保存先不正テスト()
        {
            var entry = new KeywordReserveEntry
            {
                Id = Ulid.NewUlid(),
                Keyword = "Keyword",
                RecordPath = @"C:\absolute\path",
                RecordFileName = "ok.m4a",
                SelectedDaysOfWeek = [DaysOfWeek.Monday]
            };

            var result = await _reserveLobLogic.UpdateKeywordReserveAsync(entry);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error?.Message, Is.EqualTo("指定された保存先の記載が適切ではありません。"));
        }

        [Test]
        public async ValueTask UpdateKeywordReserveAsync_ファイル名不正テスト()
        {
            var entry = new KeywordReserveEntry
            {
                Id = Ulid.NewUlid(),
                Keyword = "Keyword",
                RecordPath = @"Folder\Path",
                RecordFileName = "a/b.m4a",
                SelectedDaysOfWeek = [DaysOfWeek.Monday]
            };

            var result = await _reserveLobLogic.UpdateKeywordReserveAsync(entry);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error?.Message, Is.EqualTo("指定されたファイル名の記載が適切ではありません。"));
        }

        [Test]
        public async ValueTask UpdateKeywordReserveAsync_曜日未選択テスト()
        {
            var entry = new KeywordReserveEntry
            {
                Id = Ulid.NewUlid(),
                Keyword = "Keyword",
                RecordPath = @"Folder\Path",
                RecordFileName = "ok.m4a",
                SelectedDaysOfWeek = []
            };

            var result = await _reserveLobLogic.UpdateKeywordReserveAsync(entry);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error?.Message, Is.EqualTo("対象曜日を1つ以上選択してください。"));
        }

        [Test]
        public async ValueTask DeleteKeywordReserveAsync_削除処理テスト()
        {
            var keywordReserve = new KeywordReserve
            {
                Id = Ulid.NewUlid(),
                Keyword = "Keyword Test1",
                ExcludedKeyword = "ExcludedKeyword",
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = "filename.m4a",
                FolderPath = @"Folder\Path",
                StartTime = new TimeOnly(0, 0, 0),
                EndTime = new TimeOnly(23, 59, 59),
                DaysOfWeek = DaysOfWeek.Sunday | DaysOfWeek.Monday | DaysOfWeek.Tuesday | DaysOfWeek.Wednesday | DaysOfWeek.Thursday | DaysOfWeek.Friday | DaysOfWeek.Thursday,
                IsEnable = true,
                StartDelay = new TimeSpan(30),
                EndDelay = new TimeSpan(30)
            };

            await using var dbTran = await DbContext.Database.BeginTransactionAsync();
            try
            {
                DbContext.KeywordReserve.Add(keywordReserve);
                await DbContext.SaveChangesAsync();

                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            // Act
            var result = await _reserveLobLogic.DeleteKeywordReserveAsync(keywordReserve.Id);

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.Null);
        }

        [Test]
        public async ValueTask DeleteKeywordReserveAsync_存在しないIDテスト()
        {
            var result = await _reserveLobLogic.DeleteKeywordReserveAsync(Ulid.NewUlid());

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error?.Message, Is.EqualTo("指定されたIDのデータが見つかりません。"));
        }


        [Test]
        public async ValueTask SetKeywordReserveAsync_キーワード予約登録テスト()
        {
            // Arrange
            var entry = new KeywordReserveEntry
            {
                Id = Ulid.NewUlid(),
                Keyword = "Keyword Test2",
                ExcludedKeyword = "ExcludedKeyword2",
                SearchTitleOnly = true,
                ExcludeTitleOnly = true,
                RecordFileName = "filename2.m4a",
                RecordPath = @"Folder\Path1",
                StartTimeString = new TimeOnly(1, 0, 0).ToLongTimeString(),
                EndTimeString = new TimeOnly(22, 59, 59).ToLongTimeString(),
                SelectedDaysOfWeek = [DaysOfWeek.Wednesday],
                IsEnabled = false,
                StartDelay = 60,
                EndDelay = 60
            };

            // Act
            var result = await _reserveLobLogic.SetKeywordReserveAsync(entry);

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.Null);
        }

        [Test]
        public async ValueTask SetKeywordReserveAsync_曜日未選択テスト()
        {
            var entry = new KeywordReserveEntry
            {
                Id = Ulid.NewUlid(),
                Keyword = "Keyword Test2",
                ExcludedKeyword = "ExcludedKeyword2",
                SearchTitleOnly = true,
                ExcludeTitleOnly = true,
                RecordFileName = "filename2.m4a",
                RecordPath = @"Folder\Path1",
                StartTimeString = new TimeOnly(1, 0, 0).ToLongTimeString(),
                EndTimeString = new TimeOnly(22, 59, 59).ToLongTimeString(),
                SelectedDaysOfWeek = [],
                IsEnabled = false
            };

            var result = await _reserveLobLogic.SetKeywordReserveAsync(entry);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error?.Message, Is.EqualTo("対象曜日を1つ以上選択してください。"));
        }

        [Test]
        public async ValueTask SetKeywordReserveAsync_マージンがScheduleJobとスケジューラへ反映される()
        {
            // Arrange
            var now = _appContextMock.Object.StandardDateTimeOffset;
            var dayOfWeek = (DaysOfWeek)now.DayOfWeek;

            var programEntry = new RadikoProgram
            {
                ProgramId = "TBS_MARGIN_FLOW_001",
                Title = "Margin Flow Program",
                StartTime = now.AddHours(2),
                EndTime = now.AddHours(3),
                StationId = "TBS",
                RadioDate = DateOnly.FromDateTime(now.UtcDateTime.Date),
                DaysOfWeek = dayOfWeek,
                AvailabilityTimeFree = AvailabilityTimeFree.Unavailable
            };

            await using var dbTran = await DbContext.Database.BeginTransactionAsync();
            try
            {
                DbContext.RadikoPrograms.Add(programEntry);
                await DbContext.SaveChangesAsync();
                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            var entry = new KeywordReserveEntry
            {
                Id = Ulid.NewUlid(),
                Keyword = "Margin Flow",
                SearchTitleOnly = true,
                ExcludeTitleOnly = false,
                RecordFileName = "margin.m4a",
                RecordPath = @"Folder\Path1",
                StartTimeString = new TimeOnly(0, 0, 0).ToLongTimeString(),
                EndTimeString = new TimeOnly(23, 59, 59).ToLongTimeString(),
                SelectedDaysOfWeek = [dayOfWeek],
                SelectedRadikoStationIds = ["TBS"],
                IsEnabled = true,
                StartDelay = 90,
                EndDelay = 120
            };

            // Act
            var result = await _reserveLobLogic.SetKeywordReserveAsync(entry);

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.Null);

            var scheduled = await DbContext.ScheduleJob
                .Where(s => s.ProgramId == programEntry.ProgramId)
                .SingleOrDefaultAsync();

            Assert.That(scheduled, Is.Not.Null);
            Assert.That(scheduled!.StartDelay, Is.EqualTo(TimeSpan.FromSeconds(90)));
            Assert.That(scheduled.EndDelay, Is.EqualTo(TimeSpan.FromSeconds(120)));

            // 予約ジョブに保持されるマージン値を確認する。
            Assert.That(scheduled.StartDelay, Is.EqualTo(TimeSpan.FromSeconds(90)));
            Assert.That(scheduled.EndDelay, Is.EqualTo(TimeSpan.FromSeconds(120)));
        }

        [Test]
        public async ValueTask SetKeywordReserveAsync_放送中かつタイムフリー可能番組も予約対象になる()
        {
            // Arrange
            var now = new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.FromHours(9));
            _appContextMock.SetupGet(x => x.StandardDateTimeOffset).Returns(now);
            var dayOfWeek = (DaysOfWeek)now.DayOfWeek;

            var onAirTimeFreeProgram = new RadikoProgram
            {
                ProgramId = "TBS_ONAIR_TIMEFREE_001",
                Title = "OnAir TimeFree Program",
                StartTime = now.AddMinutes(-15),
                EndTime = now.AddMinutes(45),
                StationId = "TBS",
                RadioDate = DateOnly.FromDateTime(now.UtcDateTime.Date),
                DaysOfWeek = dayOfWeek,
                AvailabilityTimeFree = AvailabilityTimeFree.Available
            };

            await using var dbTran = await DbContext.Database.BeginTransactionAsync();
            try
            {
                DbContext.RadikoPrograms.Add(onAirTimeFreeProgram);
                await DbContext.SaveChangesAsync();
                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            var entry = new KeywordReserveEntry
            {
                Id = Ulid.NewUlid(),
                Keyword = "OnAir TimeFree",
                SearchTitleOnly = true,
                ExcludeTitleOnly = false,
                RecordFileName = "onair-timefree.m4a",
                RecordPath = @"Folder\Path1",
                StartTimeString = new TimeOnly(0, 0, 0).ToLongTimeString(),
                EndTimeString = new TimeOnly(23, 59, 59).ToLongTimeString(),
                SelectedDaysOfWeek = [dayOfWeek],
                SelectedRadikoStationIds = ["TBS"],
                IsEnabled = true
            };

            // Act
            var result = await _reserveLobLogic.SetKeywordReserveAsync(entry);

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.Null);

            var scheduled = await DbContext.ScheduleJob
                .Where(s => s.ProgramId == onAirTimeFreeProgram.ProgramId)
                .SingleOrDefaultAsync();

            Assert.That(scheduled, Is.Not.Null);
            Assert.That(scheduled!.RecordingType, Is.EqualTo(RecordingType.TimeFree));
        }

        [Test]
        public async ValueTask SetKeywordReserveAsync_放送中でタイムフリー不可でも予約対象になりリアルタイムになる()
        {
            var now = new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.FromHours(9));
            _appContextMock.SetupGet(x => x.StandardDateTimeOffset).Returns(now);
            var dayOfWeek = (DaysOfWeek)now.DayOfWeek;

            var onAirProgram = new RadikoProgram
            {
                ProgramId = "TBS_ONAIR_UNAVAILABLE_001",
                Title = "OnAir Unavailable Program",
                StartTime = now.AddMinutes(-10),
                EndTime = now.AddMinutes(50),
                StationId = "TBS",
                RadioDate = DateOnly.FromDateTime(now.UtcDateTime.Date),
                DaysOfWeek = dayOfWeek,
                AvailabilityTimeFree = AvailabilityTimeFree.Unavailable
            };

            await DbContext.RadikoPrograms.AddAsync(onAirProgram);
            await DbContext.SaveChangesAsync();

            var entry = new KeywordReserveEntry
            {
                Id = Ulid.NewUlid(),
                Keyword = "OnAir Unavailable",
                SearchTitleOnly = true,
                ExcludeTitleOnly = false,
                RecordFileName = "onair-unavailable.m4a",
                RecordPath = @"Folder\Path1",
                StartTimeString = new TimeOnly(0, 0, 0).ToLongTimeString(),
                EndTimeString = new TimeOnly(23, 59, 59).ToLongTimeString(),
                SelectedDaysOfWeek = [dayOfWeek],
                SelectedRadikoStationIds = ["TBS"],
                IsEnabled = true
            };

            var result = await _reserveLobLogic.SetKeywordReserveAsync(entry);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.Null);

            var scheduled = await DbContext.ScheduleJob
                .Where(s => s.ProgramId == onAirProgram.ProgramId)
                .SingleOrDefaultAsync();

            Assert.That(scheduled, Is.Not.Null);
            Assert.That(scheduled!.RecordingType, Is.EqualTo(RecordingType.RealTime));
        }

        [Test]
        public async ValueTask SetKeywordReserveAsync_らじるエリア優先テスト()
        {
            var now = new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.FromHours(9));
            _appContextMock.SetupGet(x => x.StandardDateTimeOffset).Returns(now);
            var dayOfWeek = (DaysOfWeek)now.DayOfWeek;
            var areaTokyo = RadiCorder.Logics.Models.NhkRadiru.RadiruAreaKind.東京.GetEnumCodeId();
            var areaSendai = RadiCorder.Logics.Models.NhkRadiru.RadiruAreaKind.仙台.GetEnumCodeId();
            var stationId = "r1";
            var eventId = "EV1";

            var programTokyo = new NhkRadiruProgram
            {
                ProgramId = "R1_TOKYO_001",
                StationId = stationId,
                AreaId = areaTokyo,
                Title = "NHK Program",
                Subtitle = "Tokyo",
                RadioDate = DateOnly.FromDateTime(now.UtcDateTime.Date),
                DaysOfWeek = dayOfWeek,
                StartTime = now.AddHours(1),
                EndTime = now.AddHours(2),
                EventId = eventId,
                SiteId = "site",
                ProgramUrl = "http://example/tokyo"
            };

            var programSendai = new NhkRadiruProgram
            {
                ProgramId = "R1_SENDAI_001",
                StationId = stationId,
                AreaId = areaSendai,
                Title = "NHK Program",
                Subtitle = "Sendai",
                RadioDate = DateOnly.FromDateTime(now.UtcDateTime.Date),
                DaysOfWeek = dayOfWeek,
                StartTime = now.AddHours(1),
                EndTime = now.AddHours(2),
                EventId = eventId,
                SiteId = "site",
                ProgramUrl = "http://example/sendai"
            };

            await using var dbTran = await DbContext.Database.BeginTransactionAsync();
            try
            {
                DbContext.NhkRadiruPrograms.AddRange(programTokyo, programSendai);
                await DbContext.SaveChangesAsync();

                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            var entry = new KeywordReserveEntry
            {
                Id = Ulid.NewUlid(),
                Keyword = "Program",
                SearchTitleOnly = true,
                ExcludeTitleOnly = false,
                RecordFileName = "filename2.m4a",
                RecordPath = @"Folder\Path1",
                StartTimeString = new TimeOnly(0, 0, 0).ToLongTimeString(),
                EndTimeString = new TimeOnly(23, 59, 59).ToLongTimeString(),
                SelectedDaysOfWeek = [dayOfWeek],
                SelectedRadiruStationIds = [$"{areaTokyo}:{stationId}", $"{areaSendai}:{stationId}"],
                IsEnabled = true
            };

            var result = await _reserveLobLogic.SetKeywordReserveAsync(entry);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.Null);

            var jobs = DbContext.ScheduleJob
                .Where(j => j.ProgramId == "R1_TOKYO_001" || j.ProgramId == "R1_SENDAI_001")
                .ToList();
            Assert.That(jobs.Count, Is.EqualTo(1));
            Assert.That(jobs[0].AreaId, Is.EqualTo(areaTokyo));
        }

        [Test]
        public async ValueTask SetAllKeywordReserveScheduleAsync_WhenCalled_ReturnsSuccess()
        {
            await _reserveLobLogic.SetAllKeywordReserveScheduleAsync();
        }

        [Test]
        public async Task ReorderKeywordReservesAsync_並び順変更時_Pendingの既存予約へ主ルールを即時反映する()
        {
            var reserveIdA = Ulid.NewUlid();
            var reserveIdB = Ulid.NewUlid();
            var scheduleJobId = Ulid.NewUlid();

            var reserveA = new KeywordReserve
            {
                Id = reserveIdA,
                Keyword = "A",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = "folder-a",
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Monday,
                StartDelay = TimeSpan.FromSeconds(10),
                EndDelay = TimeSpan.FromSeconds(20),
                SortOrder = 0
            };

            var reserveB = new KeywordReserve
            {
                Id = reserveIdB,
                Keyword = "B",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = "folder-b",
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Monday,
                StartDelay = TimeSpan.FromSeconds(30),
                EndDelay = TimeSpan.FromSeconds(40),
                SortOrder = 1
            };

            var scheduleJob = new ScheduleJob
            {
                Id = scheduleJobId,
                KeywordReserveId = reserveIdA,
                ServiceKind = RadioServiceKind.Radiko,
                StationId = "TBS",
                ProgramId = "TBS_REORDER_PENDING_001",
                FilePath = reserveA.FolderPath,
                StartDateTime = DateTimeOffset.UtcNow.AddHours(3),
                EndDateTime = DateTimeOffset.UtcNow.AddHours(4),
                Title = "Pending Job",
                StartDelay = reserveA.StartDelay,
                EndDelay = reserveA.EndDelay,
                RecordingType = RecordingType.RealTime,
                ReserveType = ReserveType.Keyword,
                IsEnabled = true,
                State = ScheduleJobState.Pending
            };

            await DbContext.KeywordReserve.AddRangeAsync(reserveA, reserveB);
            await DbContext.ScheduleJob.AddAsync(scheduleJob);
            await DbContext.ScheduleJobKeywordReserveRelations.AddRangeAsync(
            [
                new ScheduleJobKeywordReserveRelation { ScheduleJobId = scheduleJobId, KeywordReserveId = reserveIdA },
                new ScheduleJobKeywordReserveRelation { ScheduleJobId = scheduleJobId, KeywordReserveId = reserveIdB }
            ]);
            await DbContext.SaveChangesAsync();

            var orderedIds = await DbContext.KeywordReserve
                .Select(x => x.Id)
                .Where(x => x != reserveIdA && x != reserveIdB)
                .ToListAsync();
            orderedIds.Insert(0, reserveIdA);
            orderedIds.Insert(0, reserveIdB);

            var result = await _reserveLobLogic.ReorderKeywordReservesAsync(orderedIds);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.Null);

            var updated = await DbContext.ScheduleJob.FirstAsync(x => x.Id == scheduleJobId);
            Assert.That(updated.KeywordReserveId, Is.EqualTo(reserveIdB));
            Assert.That(updated.FilePath, Is.EqualTo(reserveB.FolderPath));
            Assert.That(updated.StartDelay, Is.EqualTo(reserveB.StartDelay));
            Assert.That(updated.EndDelay, Is.EqualTo(reserveB.EndDelay));
        }

        [Test]
        public async Task ReorderKeywordReservesAsync_並び順変更時_Recordingの既存予約は更新しない()
        {
            var reserveIdA = Ulid.NewUlid();
            var reserveIdB = Ulid.NewUlid();
            var scheduleJobId = Ulid.NewUlid();

            var reserveA = new KeywordReserve
            {
                Id = reserveIdA,
                Keyword = "A",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = "folder-a",
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Monday,
                StartDelay = TimeSpan.FromSeconds(10),
                EndDelay = TimeSpan.FromSeconds(20),
                SortOrder = 0
            };

            var reserveB = new KeywordReserve
            {
                Id = reserveIdB,
                Keyword = "B",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = false,
                IsExcludeTitleOnly = false,
                FileName = string.Empty,
                FolderPath = "folder-b",
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = DaysOfWeek.Monday,
                StartDelay = TimeSpan.FromSeconds(30),
                EndDelay = TimeSpan.FromSeconds(40),
                SortOrder = 1
            };

            var scheduleJob = new ScheduleJob
            {
                Id = scheduleJobId,
                KeywordReserveId = reserveIdA,
                ServiceKind = RadioServiceKind.Radiko,
                StationId = "TBS",
                ProgramId = "TBS_REORDER_RECORDING_001",
                FilePath = reserveA.FolderPath,
                StartDateTime = DateTimeOffset.UtcNow.AddHours(3),
                EndDateTime = DateTimeOffset.UtcNow.AddHours(4),
                Title = "Recording Job",
                StartDelay = reserveA.StartDelay,
                EndDelay = reserveA.EndDelay,
                RecordingType = RecordingType.RealTime,
                ReserveType = ReserveType.Keyword,
                IsEnabled = true,
                State = ScheduleJobState.Recording
            };

            await DbContext.KeywordReserve.AddRangeAsync(reserveA, reserveB);
            await DbContext.ScheduleJob.AddAsync(scheduleJob);
            await DbContext.ScheduleJobKeywordReserveRelations.AddRangeAsync(
            [
                new ScheduleJobKeywordReserveRelation { ScheduleJobId = scheduleJobId, KeywordReserveId = reserveIdA },
                new ScheduleJobKeywordReserveRelation { ScheduleJobId = scheduleJobId, KeywordReserveId = reserveIdB }
            ]);
            await DbContext.SaveChangesAsync();

            var orderedIds = await DbContext.KeywordReserve
                .Select(x => x.Id)
                .Where(x => x != reserveIdA && x != reserveIdB)
                .ToListAsync();
            orderedIds.Insert(0, reserveIdA);
            orderedIds.Insert(0, reserveIdB);

            var result = await _reserveLobLogic.ReorderKeywordReservesAsync(orderedIds);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.Null);

            var updated = await DbContext.ScheduleJob.FirstAsync(x => x.Id == scheduleJobId);
            Assert.That(updated.KeywordReserveId, Is.EqualTo(reserveIdA));
            Assert.That(updated.FilePath, Is.EqualTo(reserveA.FolderPath));
            Assert.That(updated.StartDelay, Is.EqualTo(reserveA.StartDelay));
            Assert.That(updated.EndDelay, Is.EqualTo(reserveA.EndDelay));
        }

        [Test]
        public async Task SetAllKeywordReserveScheduleAsync_既存予約より高優先ルールがあれば主ルールを昇格する()
        {
            var now = _appContextMock.Object.StandardDateTimeOffset;
            var dayOfWeek = (DaysOfWeek)now.DayOfWeek;
            var lowReserveId = Ulid.NewUlid();
            var highReserveId = Ulid.NewUlid();
            var scheduleJobId = Ulid.NewUlid();

            var program = new RadikoProgram
            {
                ProgramId = "TBS_PRIORITY_001",
                Title = "High Low Program",
                StartTime = now.AddHours(2),
                EndTime = now.AddHours(2).AddMinutes(30),
                StationId = "TBS",
                RadioDate = DateOnly.FromDateTime(now.UtcDateTime.Date),
                DaysOfWeek = dayOfWeek,
                AvailabilityTimeFree = AvailabilityTimeFree.Available
            };

            var lowReserve = new KeywordReserve
            {
                Id = lowReserveId,
                Keyword = "Low",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = true,
                IsExcludeTitleOnly = false,
                FileName = "low_$Title$",
                FolderPath = "low",
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = dayOfWeek,
                StartDelay = TimeSpan.FromSeconds(10),
                EndDelay = TimeSpan.FromSeconds(20),
                SortOrder = 10
            };

            var highReserve = new KeywordReserve
            {
                Id = highReserveId,
                Keyword = "High",
                ExcludedKeyword = string.Empty,
                IsTitleOnly = true,
                IsExcludeTitleOnly = false,
                FileName = "high_$Title$",
                FolderPath = "high",
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                IsEnable = true,
                DaysOfWeek = dayOfWeek,
                StartDelay = TimeSpan.FromSeconds(30),
                EndDelay = TimeSpan.FromSeconds(40),
                SortOrder = 1
            };

            var scheduleJob = new ScheduleJob
            {
                Id = scheduleJobId,
                KeywordReserveId = lowReserveId,
                ServiceKind = RadioServiceKind.Radiko,
                StationId = "TBS",
                AreaId = string.Empty,
                ProgramId = program.ProgramId,
                Title = program.Title,
                FilePath = lowReserve.FolderPath,
                StartDateTime = program.StartTime.ToUniversalTime(),
                EndDateTime = program.EndTime.ToUniversalTime(),
                StartDelay = lowReserve.StartDelay,
                EndDelay = lowReserve.EndDelay,
                RecordingType = RecordingType.RealTime,
                ReserveType = ReserveType.Keyword,
                IsEnabled = true,
                State = ScheduleJobState.Pending
            };

            await DbContext.RadikoPrograms.AddAsync(program);
            await DbContext.KeywordReserve.AddRangeAsync(lowReserve, highReserve);
            await DbContext.KeywordReserveRadioStations.AddRangeAsync(
            [
                new KeywordReserveRadioStation
                {
                    Id = lowReserveId,
                    RadioServiceKind = RadioServiceKind.Radiko,
                    RadioStation = "TBS"
                },
                new KeywordReserveRadioStation
                {
                    Id = highReserveId,
                    RadioServiceKind = RadioServiceKind.Radiko,
                    RadioStation = "TBS"
                }
            ]);
            await DbContext.ScheduleJob.AddAsync(scheduleJob);
            await DbContext.ScheduleJobKeywordReserveRelations.AddAsync(new ScheduleJobKeywordReserveRelation
            {
                ScheduleJobId = scheduleJobId,
                KeywordReserveId = lowReserveId
            });
            await DbContext.SaveChangesAsync();

            await _reserveLobLogic.SetAllKeywordReserveScheduleAsync();

            var updated = await DbContext.ScheduleJob.FirstAsync(x => x.Id == scheduleJobId);
            var relationIds = await DbContext.ScheduleJobKeywordReserveRelations
                .Where(x => x.ScheduleJobId == scheduleJobId)
                .Select(x => x.KeywordReserveId)
                .OrderBy(x => x)
                .ToListAsync();

            Assert.That(updated.KeywordReserveId, Is.EqualTo(highReserveId));
            Assert.That(updated.FilePath, Is.EqualTo(highReserve.FolderPath));
            Assert.That(updated.StartDelay, Is.EqualTo(highReserve.StartDelay));
            Assert.That(updated.EndDelay, Is.EqualTo(highReserve.EndDelay));
            Assert.That(relationIds, Is.EqualTo(new[] { highReserveId, lowReserveId }.OrderBy(x => x).ToList()));
        }

        [Test]
        public async ValueTask SwitchKeywordReserveEntryStatusAsync_WhenCalledWithValidId_ReturnsSuccess()
        {
            var keywordReserve = new ScheduleJob
            {
                Id = Ulid.NewUlid(),
                ServiceKind = RadioServiceKind.Radiko,
                StationId = "CBC",
                ProgramId = "CBC_9876543210123456789012345678",
                FilePath = "",
                StartDateTime = DateTime.Today.AddDays(4),
                EndDateTime = DateTime.Today.AddDays(5),
                Title = "TestTitle2",
                RecordingType = RecordingType.TimeFree,
                ReserveType = ReserveType.Program,
                IsEnabled = true
            };

            await using var dbTran = await DbContext.Database.BeginTransactionAsync();
            try
            {
                DbContext.ScheduleJob.Add(keywordReserve);
                await DbContext.SaveChangesAsync();

                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            var result = await _reserveLobLogic.SwitchKeywordReserveEntryStatusAsync(keywordReserve.Id);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.Null);
        }

        [Test]
        public async ValueTask SwitchKeywordReserveEntryStatusAsync_存在しないIDテスト()
        {
            var result = await _reserveLobLogic.SwitchKeywordReserveEntryStatusAsync(Ulid.NewUlid());

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error?.Message, Is.EqualTo("指定されたIDの予約データが見つかりません。"));
        }

        [Test]
        public async Task UpdateKeywordReserveAsync_対象なし_失敗()
        {
            var reserveRepositoryMock = new Mock<IReserveRepository>();
            reserveRepositoryMock.Setup(r => r.GetScheduleJobsByKeywordReserveIdAsync(It.IsAny<Ulid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            reserveRepositoryMock.Setup(r => r.RemoveScheduleJobsAsync(It.IsAny<IEnumerable<ScheduleJob>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            reserveRepositoryMock.Setup(r => r.DeleteKeywordReserveRadioStationsAsync(It.IsAny<Ulid>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            reserveRepositoryMock.Setup(r => r.GetKeywordReserveByIdAsync(It.IsAny<Ulid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((KeywordReserve?)null);

            var programScheduleRepoMock = new Mock<IProgramScheduleRepository>();
            var recordJobLogic = new RecordJobLobLogic(
                new Mock<ILogger<RecordJobLobLogic>>().Object,
                _configServiceMock.Object);

            var logic = new ReserveLobLogic(
                _loggerMock.Object,
                _appContextMock.Object,
                _configServiceMock.Object,
                reserveRepositoryMock.Object,
                programScheduleRepoMock.Object,
                recordJobLogic,
                _programScheduleLobLogicMock.Object,
                _notificationLobLogicMock.Object,
                new TagLobLogic(new Mock<ILogger<TagLobLogic>>().Object, DbContext),
                _entryMapper);

            var entry = new KeywordReserveEntry
            {
                Id = Ulid.NewUlid(),
                Keyword = "Keyword",
                RecordPath = @"Folder\Path",
                RecordFileName = "ok.m4a",
                SelectedDaysOfWeek = [DaysOfWeek.Monday]
            };

            var result = await logic.UpdateKeywordReserveAsync(entry);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error?.Message, Is.EqualTo("指定されたIDのデータが見つかりません。"));
        }

        [Test]
        public async Task SetRecordingJobByProgramIdAsync_保存失敗_失敗を返す()
        {
            var now = _appContextMock.Object.StandardDateTimeOffset;
            var programEntry = new RadikoProgram
            {
                ProgramId = "P_FAIL",
                Title = "Test",
                StartTime = now.AddHours(1),
                EndTime = now.AddHours(2),
                StationId = "TBS",
                RadioDate = DateOnly.FromDateTime(now.UtcDateTime.Date),
                DaysOfWeek = (DaysOfWeek)now.DayOfWeek,
                AvailabilityTimeFree = AvailabilityTimeFree.Available
            };

            await _dbContext.RadikoPrograms.AddAsync(programEntry);
            await _dbContext.SaveChangesAsync();

            var programScheduleRepository = new ProgramScheduleRepository(_dbContext);
            var programScheduleLogic = new ProgramScheduleLobLogic(
                new Mock<ILogger<ProgramScheduleLobLogic>>().Object,
                _appContextMock.Object,
                new FakeRadikoApiClient(),
                new FakeRadiruApiClient(),
                programScheduleRepository,
                _recordJobLobLogic,
                _entryMapper);

            var reserveRepositoryMock = new Mock<IReserveRepository>();
            reserveRepositoryMock.Setup(r => r.AddScheduleJobAsync(It.IsAny<ScheduleJob>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("save fail"));

            var recordJobLogic = new RecordJobLobLogic(
                new Mock<ILogger<RecordJobLobLogic>>().Object,
                _configServiceMock.Object);

            var logic = new ReserveLobLogic(
                _loggerMock.Object,
                _appContextMock.Object,
                _configServiceMock.Object,
                reserveRepositoryMock.Object,
                programScheduleRepository,
                recordJobLogic,
                programScheduleLogic,
                _notificationLobLogicMock.Object,
                new TagLobLogic(new Mock<ILogger<TagLobLogic>>().Object, DbContext),
                _entryMapper);

            var result = await logic.SetRecordingJobByProgramIdAsync("P_FAIL", RadioServiceKind.Radiko, RecordingType.RealTime);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error?.Message, Is.EqualTo("録音予約に失敗しました。"));
        }
    }
}


