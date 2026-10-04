using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Moq;
using RadiCorder.Logics.Logics.RecordedRadioLogic;
using RadiCorder.Logics.Logics.TagLogic;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Tests.LogicTest
{
    [TestFixture]
    public class RecordedRadioLobLogicTests : UnitTestBase
    {
        private Mock<ILogger<RecordedProgramQueryService>> _queryLoggerMock;
        private Mock<ILogger<RecordedProgramMediaService>> _mediaLoggerMock;
        private Mock<IAppConfigurationService> _configMock;
        private Mock<IFfmpegService> _ffmpegServiceMock;
        private IEntryMapper _entryMapper;
        private RecordedProgramQueryService _queryService;
        private RecordedProgramMediaService _mediaService;
        private RecordedRadioLobLogic _recordedRadioLogic;
        private RadioDbContext _dbContext;
        private string _mediaFixtureRoot = null!;

        [SetUp]
        public void Setup()
        {
            _queryLoggerMock = new Mock<ILogger<RecordedProgramQueryService>>();
            _mediaLoggerMock = new Mock<ILogger<RecordedProgramMediaService>>();
            _configMock = new Mock<IAppConfigurationService>();
            _ffmpegServiceMock = new Mock<IFfmpegService>();


            _mediaFixtureRoot = Path.Combine(Path.GetTempPath(), "radi-recorded-test-" + Guid.NewGuid());
            Directory.CreateDirectory(_mediaFixtureRoot);
            _configMock.SetupGet(x => x.RecordFileSaveDir).Returns(_mediaFixtureRoot);
            _configMock.SetupGet(x => x.TemporaryFileSaveDir).Returns(_mediaFixtureRoot);

            // 放送局名解決に使用するため辞書を用意
            var stations = new ConcurrentDictionary<string, string>();
            stations["TBS"] = "TBS";
            _configMock.SetupGet(x => x.RadikoStationDic).Returns(stations);
            _configMock.SetupGet(x => x.IsRadikoPremiumUser).Returns(true);

            _dbContext = DbContext;

            _entryMapper = new EntryMapper(_configMock.Object);

            _queryService = new RecordedProgramQueryService(
                _queryLoggerMock.Object,
                _entryMapper,
                new TagLobLogic(new Mock<ILogger<TagLobLogic>>().Object, DbContext),
                _configMock.Object,
                DbContext);

            _mediaService = new RecordedProgramMediaService(
                _mediaLoggerMock.Object,
                _configMock.Object,
                _ffmpegServiceMock.Object,
                DbContext);

            _recordedRadioLogic = new RecordedRadioLobLogic(
                _queryService,
                _mediaService);
        }

        [Test]
        public async Task GetRecorderProgramListAsync_WithValidParameters_ReturnsProgramList()
        {
            // Arrange
            var searchQuery = "test";
            var page = 1;
            var pageSize = 10;
            var sortBy = "Title";
            var isDescending = false;

            var recordedEntries = new List<TestRecordingEntry>
            {
                new("Test Program 1", "test1.mp3", DateTimeOffset.Now, DateTimeOffset.Now.AddHours(1)),
                new("Test Program 2", "test2.mp3", DateTimeOffset.Now, DateTimeOffset.Now.AddHours(2)),
                new("AnotherProgram", "test3.mp3", DateTimeOffset.Now, DateTimeOffset.Now.AddHours(2))
            };

            await SetRecordedEntriesAsync(recordedEntries);

            var result = await _recordedRadioLogic.GetRecorderProgramListAsync(
                searchQuery,
                page,
                pageSize,
                sortBy,
                isDescending,
                null,
                string.Empty);

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Total, Is.EqualTo(2));
            Assert.That(result.List?.Count(r => r.Title.Contains("Test")) == 2, Is.True);
            Assert.That(result.Error, Is.Null);
        }

        [Test]
        public async Task CheckProgramExistsAsync_WithExistingProgram_ReturnsTrue()
        {
            // Arrange
            var recorderId = new Ulid();

            await SetRecordedEntriesAsync(
                [new TestRecordingEntry("Test Program", "test.m4a", DateTimeOffset.Now, DateTimeOffset.Now.AddHours(1), recorderId)]);

            // Act
            var result = await _recordedRadioLogic.CheckProgramExistsAsync(recorderId);

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.IsExists, Is.True);
        }

        [Test]
        public async Task CheckProgramExistsAsync_WithNonExistingProgram_ReturnsFalse()
        {
            // Arrange
            var recorderId = new Ulid();

            // Act
            var result = await _recordedRadioLogic.CheckProgramExistsAsync(recorderId);

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.IsExists, Is.False);
        }

        [Test]
        public async Task DeleteRecordedProgramAsync_WithExistingProgram_DeletesProgramAndFiles()
        {
            var entryId = Ulid.NewUlid();
            const string relativePath = "Temp/file.m4a";
            await SetRecordedEntriesAsync(
                [new TestRecordingEntry("Test Program", relativePath, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), entryId)]);

            var data = await _recordedRadioLogic.GetRecordedProgramFilePathAsync(entryId);

            Assert.That(data.IsSuccess, Is.True);
            Assert.That(data.FilePath, Is.EqualTo(relativePath));

            var result = await _recordedRadioLogic.DeleteRecordedProgramAsync(entryId);
            Assert.That(result, Is.True);

        }

        [TearDown]
        protected async ValueTask DeleteEntry()
        {
            Directory.Delete(_mediaFixtureRoot, recursive: true);
            var dbTran = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                _dbContext.Recordings.RemoveRange(_dbContext.Recordings);
                _dbContext.RecordingMetadatas.RemoveRange(_dbContext.RecordingMetadatas);
                _dbContext.RecordingFiles.RemoveRange(_dbContext.RecordingFiles);

                await _dbContext.SaveChangesAsync();
                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }
        }


        private async ValueTask SetRecordedEntriesAsync(List<TestRecordingEntry> list)
        {
            var dbTran = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                foreach (var item in list)
                {
                    var recordingId = item.RecordingId ?? Ulid.NewUlid();

                    var recording = new Recording
                    {
                        Id = recordingId,
                        ServiceKind = Models.Enums.RadioServiceKind.Radiko,
                        ProgramId = "TEST",
                        StationId = "TBS",
                        AreaId = "JP13",
                        StartDateTime = item.StartDateTime.UtcDateTime,
                        EndDateTime = item.EndDateTime.UtcDateTime,
                        IsTimeFree = false,
                        State = RecordingState.Completed,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };

                    var metadata = new RecordingMetadata
                    {
                        RecordingId = recordingId,
                        Title = item.Title,
                        Subtitle = "",
                        Performer = "",
                        Description = "",
                        ProgramUrl = ""
                    };

                    var file = new RecordingFile
                    {
                        RecordingId = recordingId,
                        FileRelativePath = item.FilePath,
                        HasHlsFile = false
                    };

                    await _dbContext.Recordings.AddAsync(recording);
                    await _dbContext.RecordingMetadatas.AddAsync(metadata);
                    await _dbContext.RecordingFiles.AddAsync(file);
                }
                await _dbContext.SaveChangesAsync();
                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }
        }

        private record TestRecordingEntry(
            string Title,
            string FilePath,
            DateTimeOffset StartDateTime,
            DateTimeOffset EndDateTime,
            Ulid? RecordingId = null);
    }
}
