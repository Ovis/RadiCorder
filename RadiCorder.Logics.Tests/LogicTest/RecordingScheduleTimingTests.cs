using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Tests.LogicTest;

public class RecordingScheduleTimingTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-04-01T05:00:00+09:00");

    [TestCase(RecordingType.RealTime, 30, -31)]
    [TestCase(RecordingType.RealTime, -30, 29)]
    [TestCase(RecordingType.TimeFree, 30, 3780)]
    [TestCase(RecordingType.Immediate, 30, 0)]
    [TestCase(RecordingType.OnDemand, 30, 0)]
    public void ResolveFireAtUtc_録音種別と開始ディレイの規則を維持する(RecordingType type, int delay, int expectedSeconds)
    {
        var result = RecordingScheduleTiming.ResolveFireAtUtc(type, Start, Start.AddHours(1), TimeSpan.FromSeconds(delay), Start.ToUniversalTime());
        Assert.That(result, Is.EqualTo(Start.ToUniversalTime().AddSeconds(expectedSeconds)));
        Assert.That(result!.Value.Offset, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void ResolveFireAtUtc_タイムフリー待機時刻を過ぎていれば即時実行する()
    {
        var now = Start.AddHours(2).ToUniversalTime();
        Assert.That(RecordingScheduleTiming.ResolveFireAtUtc(RecordingType.TimeFree, Start, Start.AddHours(1), TimeSpan.Zero, now), Is.EqualTo(now));
        Assert.That(RecordingScheduleTiming.ResolveFireAtUtc(RecordingType.Undefined, Start, Start.AddHours(1), TimeSpan.Zero, now), Is.Null);
        Assert.That(RecordingScheduleTiming.PreparingLeadTime, Is.EqualTo(TimeSpan.FromSeconds(10)));
    }

    [TestCase("認証 ffmpeg", ScheduleJobErrorCode.AuthFailed)]
    [TestCase("ffmpeg disk", ScheduleJobErrorCode.FfmpegFailed)]
    [TestCase("disk I/O", ScheduleJobErrorCode.DiskFull)]
    [TestCase("I/O playlist", ScheduleJobErrorCode.IoError)]
    [TestCase("playlist", ScheduleJobErrorCode.SourceUnavailable)]
    [TestCase("", ScheduleJobErrorCode.Unknown)]
    public void ClassifyError_従来の分類優先順を維持する(string message, ScheduleJobErrorCode code)
    {
        Assert.That(RecordingJobErrorClassifier.ClassifyError(new Exception(message)), Is.EqualTo(code));
    }
}
