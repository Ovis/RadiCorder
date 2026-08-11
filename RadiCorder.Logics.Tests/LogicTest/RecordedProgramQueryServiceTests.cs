using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Moq;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Logics.RecordedRadioLogic;
using RadiCorder.Logics.Logics.TagLogic;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Tests.LogicTest;

/// <summary>
/// RecordedProgramQueryServiceのテスト
/// </summary>
public class RecordedProgramQueryServiceTests : UnitTestBase
{
    private Mock<ILogger<RecordedProgramQueryService>> _loggerMock = null!;
    private Mock<IAppConfigurationService> _configMock = null!;
    private RecordedProgramQueryService _service = null!;
    private RadioDbContext _dbContext = null!;

    [SetUp]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<RecordedProgramQueryService>>();
        _configMock = new Mock<IAppConfigurationService>();
        _dbContext = DbContext;

        // 放送局名解決に使用する辞書
        var stations = new ConcurrentDictionary<string, string>();
        stations["TBS"] = "TBS";
        _configMock.SetupGet(x => x.RadikoStationDic).Returns(stations);

        var mapper = new EntryMapper(_configMock.Object);
        var tagLogic = new TagLobLogic(new Mock<ILogger<TagLobLogic>>().Object, _dbContext);
        _service = new RecordedProgramQueryService(_loggerMock.Object, mapper, tagLogic, _configMock.Object, _dbContext);
    }

    /// <summary>
    /// Durationで昇順ソートできる
    /// </summary>
    [Test]
    public async Task GetRecorderProgramListAsync_Duration昇順()
    {
        await AddRecordingAsync("Short", 60);
        await AddRecordingAsync("Long", 120);

        var result = await _service.GetRecorderProgramListAsync("", 1, 10, "Duration", false, null, string.Empty);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.List, Is.Not.Null);
        Assert.That(result.List![0].Title, Is.EqualTo("Short"));
        Assert.That(result.List[1].Title, Is.EqualTo("Long"));
    }

    /// <summary>
    /// Durationで降順ソートできる
    /// </summary>
    [Test]
    public async Task GetRecorderProgramListAsync_Duration降順()
    {
        await AddRecordingAsync("Short", 60);
        await AddRecordingAsync("Long", 120);

        var result = await _service.GetRecorderProgramListAsync("", 1, 10, "Duration", true, null, string.Empty);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.List, Is.Not.Null);
        Assert.That(result.List![0].Title, Is.EqualTo("Long"));
        Assert.That(result.List[1].Title, Is.EqualTo("Short"));
    }

    [Test]
    public async Task GetRecorderProgramListAsync_タグOR検索()
    {
        var rec1 = await AddRecordingAsync("Tag A Program", 60);
        var rec2 = await AddRecordingAsync("Tag B Program", 120);
        var tagA = await AddTagAsync("A");
        var tagB = await AddTagAsync("B");
        await AddRecordingTagAsync(rec1, tagA);
        await AddRecordingTagAsync(rec2, tagB);

        var result = await _service.GetRecorderProgramListAsync("", 1, 10, "StartDateTime", false, null, string.Empty, [tagA, tagB], "or", false);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Total, Is.EqualTo(2));
    }

    [Test]
    public async Task GetRecorderProgramListAsync_タグAND検索()
    {
        var rec1 = await AddRecordingAsync("Tag AB Program", 60);
        var rec2 = await AddRecordingAsync("Tag B Program", 120);
        var tagA = await AddTagAsync("A");
        var tagB = await AddTagAsync("B");
        await AddRecordingTagAsync(rec1, tagA);
        await AddRecordingTagAsync(rec1, tagB);
        await AddRecordingTagAsync(rec2, tagB);

        var result = await _service.GetRecorderProgramListAsync("", 1, 10, "StartDateTime", false, null, string.Empty, [tagA, tagB], "and", false);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Total, Is.EqualTo(1));
        Assert.That(result.List?.Single().Title, Is.EqualTo("Tag AB Program"));
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
            _dbContext.RecordingTagRelations.RemoveRange(_dbContext.RecordingTagRelations);
            _dbContext.RecordingTags.RemoveRange(_dbContext.RecordingTags);

            await _dbContext.SaveChangesAsync();
            await tran.CommitAsync();
        }
        catch
        {
            await tran.RollbackAsync();
        }
    }

    /// <summary>
    /// 録音データを登録
    /// </summary>
    private async Task<Ulid> AddRecordingAsync(string title, int durationSeconds)
    {
        var id = Ulid.NewUlid();
        var start = DateTimeOffset.UtcNow;
        var end = start.AddSeconds(durationSeconds);

        var recording = new Recording
        {
            Id = id,
            ServiceKind = RadioServiceKind.Radiko,
            ProgramId = "TEST",
            StationId = "TBS",
            AreaId = "JP13",
            StartDateTime = start.UtcDateTime,
            EndDateTime = end.UtcDateTime,
            IsTimeFree = false,
            State = RecordingState.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var metadata = new RecordingMetadata
        {
            RecordingId = id,
            Title = title,
            Subtitle = "",
            Performer = "",
            Description = "",
            ProgramUrl = ""
        };

        var file = new RecordingFile
        {
            RecordingId = id,
            FileRelativePath = $"{title}.m4a",
            HasHlsFile = false
        };

        await _dbContext.Recordings.AddAsync(recording);
        await _dbContext.RecordingMetadatas.AddAsync(metadata);
        await _dbContext.RecordingFiles.AddAsync(file);
        await _dbContext.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> AddTagAsync(string name)
    {
        var id = Guid.NewGuid();
        await _dbContext.RecordingTags.AddAsync(new RecordingTag
        {
            Id = id,
            Name = name,
            NormalizedName = name.ToLowerInvariant(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();
        return id;
    }

    private async Task AddRecordingTagAsync(Ulid recordingId, Guid tagId)
    {
        await _dbContext.RecordingTagRelations.AddAsync(new RecordingTagRelation
        {
            RecordingId = recordingId,
            TagId = tagId
        });
        await _dbContext.SaveChangesAsync();
    }
}
