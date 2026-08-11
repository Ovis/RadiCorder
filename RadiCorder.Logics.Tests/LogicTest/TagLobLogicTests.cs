using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Moq;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Logics.TagLogic;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Tests.LogicTest;

public class TagLobLogicTests : UnitTestBase
{
    private TagLobLogic _logic = null!;

    [SetUp]
    public void Setup()
    {
        _logic = new TagLobLogic(new Mock<ILogger<TagLobLogic>>().Object, DbContext);
    }

    [TearDown]
    public async Task TearDown()
    {
        DbContext.RecordingTagRelations.RemoveRange(DbContext.RecordingTagRelations);
        DbContext.KeywordReserveTagRelations.RemoveRange(DbContext.KeywordReserveTagRelations);
        DbContext.RecordingTags.RemoveRange(DbContext.RecordingTags);
        DbContext.KeywordReserve.RemoveRange(DbContext.KeywordReserve);
        DbContext.RecordingFiles.RemoveRange(DbContext.RecordingFiles);
        DbContext.RecordingMetadatas.RemoveRange(DbContext.RecordingMetadatas);
        DbContext.Recordings.RemoveRange(DbContext.Recordings);
        await DbContext.SaveChangesAsync();
    }

    [Test]
    public async Task CreateTagAsync_正規化重複は拒否される()
    {
        await _logic.CreateTagAsync("  Ｔｅｓｔ ・ Tag ");

        var ex = Assert.ThrowsAsync<DomainException>(async () => await _logic.CreateTagAsync("test ･ tag"));
        Assert.That(ex?.UserMessage, Is.EqualTo("同じタグ名が既に存在します。"));
    }

    [Test]
    public async Task MergeTagAsync_中間テーブルを付け替える()
    {
        var from = await _logic.CreateTagAsync("from");
        var to = await _logic.CreateTagAsync("to");
        var recordingId = await AddRecordingAsync("Merge Test");
        var reserveId = await AddKeywordReserveAsync();

        await _logic.AddTagsToRecordingAsync(recordingId, [from.Id]);
        await _logic.SetKeywordReserveTagsAsync(reserveId, [from.Id]);

        await _logic.MergeTagAsync(from.Id, to.Id);

        var existsFrom = await DbContext.RecordingTags.AnyAsync(t => t.Id == from.Id);
        var recordingRel = await DbContext.RecordingTagRelations.AnyAsync(r => r.RecordingId == recordingId && r.TagId == to.Id);
        var reserveRel = await DbContext.KeywordReserveTagRelations.AnyAsync(r => r.ReserveId == reserveId && r.TagId == to.Id);
        Assert.That(existsFrom, Is.False);
        Assert.That(recordingRel, Is.True);
        Assert.That(reserveRel, Is.True);
    }

    [Test]
    public async Task DeleteTagAsync_録音本体は削除されない()
    {
        var tag = await _logic.CreateTagAsync("delete-check");
        var recordingId = await AddRecordingAsync("Delete Check");
        await _logic.AddTagsToRecordingAsync(recordingId, [tag.Id]);

        await _logic.DeleteTagAsync(tag.Id);

        var recordingExists = await DbContext.Recordings.AnyAsync(r => r.Id == recordingId);
        Assert.That(recordingExists, Is.True);
    }

    [Test]
    public async Task BulkAddTagsToRecordingsAsync_重複付与でも行が増えない()
    {
        var tag = await _logic.CreateTagAsync("dup");
        var recordingId = await AddRecordingAsync("Bulk Add");

        var first = await _logic.BulkAddTagsToRecordingsAsync([recordingId], [tag.Id]);
        var second = await _logic.BulkAddTagsToRecordingsAsync([recordingId], [tag.Id]);
        var count = await DbContext.RecordingTagRelations.CountAsync(r => r.RecordingId == recordingId && r.TagId == tag.Id);

        Assert.That(first.SuccessCount, Is.EqualTo(1));
        Assert.That(second.SkipCount, Is.EqualTo(1));
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task BulkRemoveTagsFromRecordingsAsync_未付与IDが混在しても成功する()
    {
        var tag = await _logic.CreateTagAsync("bulk-remove");
        var recording1 = await AddRecordingAsync("Bulk Remove 1");
        var recording2 = await AddRecordingAsync("Bulk Remove 2");
        await _logic.AddTagsToRecordingAsync(recording1, [tag.Id]);

        var result = await _logic.BulkRemoveTagsFromRecordingsAsync([recording1, recording2], [tag.Id]);

        Assert.That(result.SuccessCount, Is.EqualTo(1));
        Assert.That(result.SkipCount, Is.EqualTo(1));
        Assert.That(result.FailCount, Is.EqualTo(0));
    }

    [Test]
    public void BulkAddTagsToRecordingsAsync_0件選択はバリデーションエラー()
    {
        var ex = Assert.ThrowsAsync<DomainException>(async () =>
            await _logic.BulkAddTagsToRecordingsAsync([], [Guid.NewGuid()]));

        Assert.That(ex?.UserMessage, Is.EqualTo("録音が選択されていません。"));
    }

    private async ValueTask<Ulid> AddRecordingAsync(string title)
    {
        var id = Ulid.NewUlid();
        var start = DateTimeOffset.UtcNow.AddMinutes(-30);
        var end = DateTimeOffset.UtcNow.AddMinutes(-1);

        await DbContext.Recordings.AddAsync(new Recording
        {
            Id = id,
            ServiceKind = RadioServiceKind.Radiko,
            ProgramId = $"P-{id}",
            StationId = "TBS",
            AreaId = "JP13",
            StartDateTime = start.UtcDateTime,
            EndDateTime = end.UtcDateTime,
            IsTimeFree = false,
            State = Domain.Recording.RecordingState.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await DbContext.RecordingMetadatas.AddAsync(new RecordingMetadata
        {
            RecordingId = id,
            Title = title,
            Subtitle = string.Empty,
            Performer = string.Empty,
            Description = string.Empty,
            ProgramUrl = string.Empty
        });

        await DbContext.RecordingFiles.AddAsync(new RecordingFile
        {
            RecordingId = id,
            FileRelativePath = $"{id}.m4a",
            HasHlsFile = false
        });

        await DbContext.SaveChangesAsync();
        return id;
    }

    private async ValueTask<Ulid> AddKeywordReserveAsync()
    {
        var id = Ulid.NewUlid();
        await DbContext.KeywordReserve.AddAsync(new KeywordReserve
        {
            Id = id,
            Keyword = "k",
            ExcludedKeyword = string.Empty,
            IsTitleOnly = false,
            IsExcludeTitleOnly = false,
            FileName = string.Empty,
            FolderPath = string.Empty,
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            IsEnable = true,
            DaysOfWeek = Models.Enums.DaysOfWeek.Monday
        });
        await DbContext.SaveChangesAsync();
        return id;
    }
}
