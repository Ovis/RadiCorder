using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RadiCorder.Logics.Infrastructure.Import;
using RadiCorder.Logics.Models.ExternalImport;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Tests.LogicTest;

public class ExternalImportAdaptersTests
{
    [Test]
    public void Template_放送局とタイトルと日本時間をDBなしで補完できる()
    {
        var config = new Mock<IAppConfigurationService>();
        config.SetupGet(x => x.RecordDirectoryRelativePath).Returns("$StationName$/$Title$");
        config.SetupGet(x => x.RecordFileNameTemplate).Returns("$SYYYY$$SMM$$SDD$$STHH$$STMM$$STSS$_$Title$");
        var parser = new ExternalImportTemplateParser(NullLogger.Instance, config.Object);
        var success = parser.TryEnrichFromTemplates("放送局/番組/20260401050000_番組.mp3", out var station, out var title, out var broadcastAt);
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(station, Is.EqualTo("放送局"));
            Assert.That(title, Is.EqualTo("番組"));
            Assert.That(broadcastAt, Is.EqualTo(DateTimeOffset.Parse("2026-04-01T05:00:00+09:00")));
        });
    }

    [Test]
    public async Task Csv_複数行と引用符と数式対策を維持する()
    {
        var root = Path.Combine(Path.GetTempPath(), $"radicorder-csv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "audio.mp3"), [1, 2]);
            var source = new ExternalImportCandidateEntry
            {
                FilePath = "audio.mp3", Title = "=数式", Description = "一行目\n\"二行目\"",
                StationName = "放送局", BroadcastAt = DateTimeOffset.Parse("2026-04-01T05:00:00+09:00"), Tags = ["外部取込"]
            };
            await using var stream = new MemoryStream(ExternalImportCsv.ExportCandidatesCsv([source]));
            var (success, candidates, errors) = await ExternalImportCsv.ImportCandidatesCsvAsync(stream, root);
            Assert.That(success, Is.True);
            Assert.That(errors, Is.Empty);
            Assert.That(candidates, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(candidates[0].Title, Is.EqualTo("'=数式"));
                Assert.That(candidates[0].Description, Is.EqualTo(source.Description));
                Assert.That(candidates[0].BroadcastAt, Is.EqualTo(source.BroadcastAt));
                Assert.That(candidates[0].Tags, Is.EqualTo(source.Tags));
                Assert.That(stream.CanRead, Is.True);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("../outside.mp3")]
    [TestCase("nested/../outside.mp3")]
    [TestCase("nested/./audio.mp3")]
    [TestCase("..\\outside.mp3")]
    public void Paths_親階層への参照を拒否する(string path)
    {
        Assert.That(ExternalImportPaths.TryResolveManagedFilePath(path, Path.GetTempPath(), out _, out _), Is.False);
    }
}
