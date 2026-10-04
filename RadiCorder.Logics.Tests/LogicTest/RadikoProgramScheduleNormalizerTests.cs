using System.Xml.Linq;
using RadiCorder.Logics.Providers.Radiko;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Tests.LogicTest;

public class RadikoProgramScheduleNormalizerTests
{
    [Test]
    public void 長さ0秒を同じ局の後続番組から推定し元のIDと診断情報を保持する()
    {
        var start = DateTimeOffset.Parse("2026-10-06T03:00:00+09:00");
        var zero = Create("zero", "ABC", start, start);
        var next = Create("next", "ABC", start.AddMinutes(80), start.AddMinutes(95));
        var otherStation = Create("other", "CRT", start.AddMinutes(10), start.AddMinutes(20));
        var invalid = Create("invalid", "ABC", start.AddMinutes(5), start.AddMinutes(1));
        var (programs, warnings) = RadikoProgramScheduleNormalizer.Normalize([next, otherStation, invalid, zero]);

        Assert.That(programs.Select(p => p.ProgramId), Is.EquivalentTo(new[] { "zero", "next", "other" }));
        Assert.That(zero.EndTime, Is.EqualTo(next.StartTime));
        var estimate = warnings.Single(w => w.ProgramId == "zero");
        Assert.That(estimate.EndTime, Is.EqualTo(start));
        Assert.That(estimate.EstimatedEndTime, Is.EqualTo(next.StartTime));
        Assert.That(warnings.Any(w => w.ProgramId == "invalid" && w.EstimatedEndTime == null), Is.True);
    }

    [Test]
    public void 同時刻の番組だけでは推定せず後続がない長さ0秒の番組をスキップする()
    {
        var start = DateTimeOffset.Parse("2026-10-06T03:00:00+09:00");
        var (programs, warnings) = RadikoProgramScheduleNormalizer.Normalize([
            Create("zero", "ABC", start, start), Create("same", "ABC", start, start),
            Create("other", "CRT", start.AddHours(1), start.AddHours(2))]);
        Assert.That(programs.Select(p => p.ProgramId), Is.EqualTo(new[] { "other" }));
        Assert.That(warnings.Count, Is.EqualTo(2));
        Assert.That(warnings.All(w => w.EstimatedEndTime == null), Is.True);
    }

    [Test]
    public void 連続する長さ0秒も各開始時刻を根拠に推定する()
    {
        var start = DateTimeOffset.Parse("2026-10-06T03:00:00+09:00");
        var first = Create("first", "ABC", start, start);
        var second = Create("second", "ABC", start.AddHours(1), start.AddHours(1));
        var third = Create("third", "ABC", start.AddHours(2), start.AddHours(3));
        var (programs, warnings) = RadikoProgramScheduleNormalizer.Normalize([third, first, second]);
        Assert.That(first.EndTime, Is.EqualTo(second.StartTime));
        Assert.That(second.EndTime, Is.EqualTo(third.StartTime));
        Assert.That(programs.Count, Is.EqualTo(3));
        Assert.That(warnings.Count, Is.EqualTo(2));
    }

    [TestCase("<title />")]
    [TestCase("")]
    [TestCase("<title>  </title>")]
    public void 番組名の空欄は時刻が有効なら取り込み警告を残す(string title)
    {
        var xml = XElement.Parse($"<prog ft='20261007144000' to='20261007145500'>{title}<ts_in_ng>0</ts_in_ng></prog>");
        var parsed = RadikoProgramParser.ParseProgram(xml, "CRT").Program!;
        var (programs, warnings) = RadikoProgramScheduleNormalizer.Normalize([parsed]);
        Assert.That(programs.Single().Title, Is.EqualTo(string.Empty));
        Assert.That(warnings.Single().Reason, Does.Contain("番組名"));
    }

    private static RadikoProgram Create(string id, string station, DateTimeOffset start, DateTimeOffset end)
        => new() { ProgramId = id, StationId = station, Title = "番組", StartTime = start, EndTime = end };
}
