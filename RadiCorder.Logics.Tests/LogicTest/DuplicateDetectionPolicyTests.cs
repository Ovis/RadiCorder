using Microsoft.Extensions.Logging.Abstractions;
using RadiCorder.Logics.Domain.DuplicateDetection;

namespace RadiCorder.Logics.Tests.LogicTest;

public class DuplicateDetectionPolicyTests
{
    [TestCase("番組Ａ （再放送）", "番組a")]
    [TestCase(" NEWS [Special] ！", "news")]
    [TestCase("", "")]
    public void NormalizeTitle_従来の正規化を維持する(string input, string expected)
    {
        Assert.That(DuplicateSimilarity.NormalizeTitle(input), Is.EqualTo(expected));
    }

    [Test]
    public void Correlation_最小重複時間と無音の扱いを維持する()
    {
        Assert.That(DuplicateSimilarity.CalculateBestCorrelationScore(new double[119], new double[200]), Is.Zero);
        Assert.That(DuplicateSimilarity.CalculateBestCorrelationScore(new double[200], new double[200]), Is.EqualTo(0.5));
        var samples = Enumerable.Range(0, 240).Select(i => Math.Sin(i * 0.13) + Math.Cos(i * 0.27)).ToArray();
        Assert.That(DuplicateSimilarity.CalculateBestCorrelationScore(samples, samples), Is.EqualTo(1).Within(1e-12));
    }

    [Test]
    public void BuildEnergyBins_最小値のPCMと端数を安全に処理する()
    {
        var bytes = new byte[DuplicateSimilarity.AudioSampleRate * 2 + 1];
        for (var index = 0; index + 1 < bytes.Length; index += 2)
        {
            bytes[index] = 0;
            bytes[index + 1] = 128;
        }
        Assert.That(DuplicateSimilarity.BuildEnergyBins(bytes), Is.EqualTo(new[] { 32768d }));
        Assert.That(DuplicateSimilarity.BuildEnergyBins([1]), Is.Empty);
    }

    [Test]
    public void Groups_放送回の時間窓ちょうどで分割する()
    {
        var items = new List<DuplicateRecording> { Recording(0), Recording(167), Recording(168), Recording(169) };
        var groups = DuplicateCandidateSelector.BuildPhase1Groups(items, 168, NullLogger.Instance);
        Assert.That(groups, Has.Count.EqualTo(2));
        Assert.That(groups[0].MemberIds, Is.EquivalentTo(new[] { items[0].RecordingId, items[1].RecordingId }));
        Assert.That(groups[1].MemberIds, Is.EquivalentTo(new[] { items[2].RecordingId, items[3].RecordingId }));
    }

    [Test]
    public void Targets_lightは代表候補のみでstrictは全組合せとする()
    {
        var items = new List<DuplicateRecording> { Recording(0), Recording(1), Recording(2) };
        var groups = DuplicateCandidateSelector.BuildPhase1Groups(items, 168, NullLogger.Instance);
        Assert.That(DuplicateCandidateSelector.BuildPhase2TargetsByGroup(groups, 1, strictMode: true), Has.Count.EqualTo(3));
        var compact = DuplicateCandidateSelector.BuildPhase2TargetsByGroup(groups, 1, strictMode: false);
        Assert.That(compact, Has.Count.EqualTo(2));
        Assert.That(compact.All(x => x.Left.RecordingId == items[1].RecordingId || x.Right.RecordingId == items[1].RecordingId), Is.True);
    }

    [Test]
    public void Candidates_同じ局と短すぎる候補を比較対象にしない()
    {
        var first = Recording(0);
        Assert.That(DuplicateCandidateSelector.BuildPhase1Candidates([first, Recording(1) with { StationId = first.StationId }]), Is.Empty);
        Assert.That(DuplicateCandidateSelector.BuildPhase1Candidates([first, Recording(1) with { DurationSeconds = 600 }]), Is.Empty);
    }

    private static DuplicateRecording Recording(int hour) => new()
    {
        RecordingId = Ulid.NewUlid(), StationId = $"station-{hour}", Title = "同じ番組", NormalizedTitle = "同じ番組",
        StartDateTime = DateTimeOffset.Parse("2026-04-01T00:00:00Z").AddHours(hour), DurationSeconds = 3600
    };
}
