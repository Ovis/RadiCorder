using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RadiCorder.Logics.Services.Streaming;

namespace RadiCorder.Logics.Tests.LogicTest;

/// <summary>
/// 改修前の実装から取得したプレイリストfixtureとの一致を確認する
/// </summary>
public class RadikoPlaylistProcessorTests
{
    public static IEnumerable<TestCaseData> PlaylistCases()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "hls-baseline.json");
        foreach (var entry in JsonSerializer.Deserialize<List<PlaylistCase>>(File.ReadAllText(path))!)
        {
            yield return new TestCaseData(entry).SetName($"Playlist_{entry.Name}_{entry.RecordingStart:HHmmss}");
        }
    }

    [TestCaseSource(nameof(PlaylistCases))]
    public void Playlist_録音開始時刻と書換結果を維持する(PlaylistCase entry)
    {
        var trimmed = RadikoPlaylistProcessor.TrimLiveMediaPlaylistForRecording(
            entry.Input, entry.Now, NullLogger.Instance, entry.RecordingStart);
        var rewritten = RadikoPlaylistProcessor.RewritePlaylistToLocalProxy(
            trimmed, new Uri("https://radiko.jp/live/master.m3u8"), "fixture-key");
        Assert.Multiple(() =>
        {
            Assert.That(trimmed, Is.EqualTo(entry.Trimmed));
            Assert.That(rewritten, Is.EqualTo(entry.Rewritten));
        });
    }

    [TestCase("https://radiko.jp/live.m3u8", true)]
    [TestCase("https://a.radiko.jp/live.m3u8", true)]
    [TestCase("https://a.smartstream.ne.jp/live.m3u8", true)]
    [TestCase("https://a.radiko-cf.com/live.m3u8", true)]
    [TestCase("http://radiko.jp/live.m3u8", false)]
    [TestCase("https://radiko.jp.example.com/live.m3u8", false)]
    [TestCase("https://example.com/radiko.jp/live.m3u8", false)]
    public void ProxyTarget_既存の許可条件を維持する(string target, bool expected)
    {
        Assert.That(RadikoPlaylistProcessor.IsAllowedRadikoProxyTarget(new Uri(target)), Is.EqualTo(expected));
    }

    public sealed record PlaylistCase(
        string Name, string Input, DateTimeOffset Now, DateTimeOffset? RecordingStart, string Trimmed, string Rewritten);
}
