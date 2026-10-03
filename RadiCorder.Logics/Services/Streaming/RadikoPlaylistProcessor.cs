using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace RadiCorder.Logics.Services.Streaming;

/// <summary>
/// radiko のプレイリストを解析・整形する。通信やHTTP応答は扱わない。
/// </summary>
public static class RadikoPlaylistProcessor
{
    private static readonly TimeSpan LivePlaylistProgramDateTimeLookback = TimeSpan.FromSeconds(12);
    private const int LivePlaylistFallbackSegmentCount = 3;
    private const int LivePlaylistLiveEdgeSegmentCount = 2;


    /// <summary>
    /// 最後のセグメントの終了時刻を取得する。
    /// </summary>
    public static DateTimeOffset? GetLastSegmentEndUtc(string playlist) =>
        ParseLiveMediaPlaylist(playlist).SegmentBlocks.LastOrDefault()?.EndTimeUtc;

    public static bool IsAllowedRadikoProxyTarget(Uri uri)
    {
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.Host;
        return host.Equals("radiko.jp", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".radiko.jp", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".smartstream.ne.jp", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".radiko-cf.com", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPlaylistRequest(Uri targetUri, string? mediaType)
    {
        if (!string.IsNullOrWhiteSpace(mediaType) &&
            (mediaType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) ||
             mediaType.Contains("vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return targetUri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
    }

    public static string RewritePlaylistToLocalProxy(string content, Uri baseUri, string proxyKey)
    {
        var normalized = content.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var isMasterPlaylist = normalized.Contains("#EXT-X-STREAM-INF", StringComparison.Ordinal);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith('#'))
            {
                lines[i] = RewriteTagLineUris(line, baseUri, proxyKey);
                continue;
            }

            lines[i] = isMasterPlaylist
                ? RadikoProxyUrlUtility.BuildRelativeProxyUrlWithProxyKey(baseUri.ToString(), proxyKey, resolveLivePlaylist: true)
                : RadikoProxyUrlUtility.BuildRelativeProxyUrlWithProxyKey(new Uri(baseUri, line).ToString(), proxyKey);
        }

        return string.Join('\n', lines);
    }

    private static string RewriteTagLineUris(string line, Uri baseUri, string proxyKey)
    {
        return Regex.Replace(
            line,
            "URI=\"([^\"]+)\"",
            match =>
            {
                var value = match.Groups[1].Value;
                var resolved = new Uri(baseUri, value).ToString();
                var proxied = RadikoProxyUrlUtility.BuildRelativeProxyUrlWithProxyKey(resolved, proxyKey);
                return $"URI=\"{proxied}\"";
            });
    }

    public static Uri? ExtractFirstPlaylistUri(string playlist, Uri baseUri)
    {
        var normalized = playlist.Replace("\r\n", "\n");
        foreach (var line in normalized.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            return new Uri(baseUri, line);
        }

        return null;
    }

    public static string TrimLiveMediaPlaylistForRecording(
        string playlist,
        DateTimeOffset nowUtc,
        ILogger logger,
        DateTimeOffset? recordingStartUtc)
    {
        var parseResult = ParseLiveMediaPlaylist(playlist);
        if (parseResult.IsMasterPlaylist || parseResult.Lines.Count == 0)
        {
            logger.ZLogDebug($"radiko live playlist trim skipped. reason=empty-or-master nowUtc={nowUtc:O} lineCount={parseResult.Lines.Count}");
            return playlist;
        }

        if (parseResult.FirstSegmentLineIndex < 0)
        {
            logger.ZLogDebug($"radiko live playlist trim skipped. reason=no-segment nowUtc={nowUtc:O} lineCount={parseResult.Lines.Count}");
            return playlist;
        }

        if (parseResult.SegmentBlocks.Count == 0)
        {
            logger.ZLogDebug($"radiko live playlist trim skipped. reason=no-block nowUtc={nowUtc:O} lineCount={parseResult.Lines.Count}");
            return parseResult.Normalized;
        }

        var segmentBlocks = parseResult.SegmentBlocks;
        var keepStartIndex = Math.Max(0, segmentBlocks.Count - LivePlaylistFallbackSegmentCount);
        var threshold = nowUtc - LivePlaylistProgramDateTimeLookback;
        var thresholdIndex = segmentBlocks.FindIndex(block =>
            block.EndTimeUtc is { } endTimeUtc && endTimeUtc >= threshold);
        var firstOriginalBlock = segmentBlocks.FirstOrDefault();
        var lastOriginalBlock = segmentBlocks.LastOrDefault();
        var trimMode = thresholdIndex >= 0 ? "program-date-time" : "fallback-tail";

        if (recordingStartUtc.HasValue)
        {
            var tailStartIndex = Math.Max(0, segmentBlocks.Count - LivePlaylistLiveEdgeSegmentCount);
            var recordingStartIndex = segmentBlocks.FindIndex(block =>
                block.EndTimeUtc is { } endTimeUtc && endTimeUtc >= recordingStartUtc.Value);
            keepStartIndex = recordingStartIndex >= 0
                ? Math.Max(tailStartIndex, recordingStartIndex)
                : tailStartIndex;
            trimMode = recordingStartIndex >= 0
                ? "recording-start-live-edge"
                : "live-edge-tail";
        }
        else if (thresholdIndex >= 0)
        {
            keepStartIndex = thresholdIndex;
        }

        if (keepStartIndex <= 0)
        {
            logger.ZLogDebug(
                $"radiko live playlist trim result. mode=keep-all thresholdUtc={threshold:O} recordingStartUtc={recordingStartUtc:O} nowUtc={nowUtc:O} originalSegments={segmentBlocks.Count} thresholdIndex={thresholdIndex} keepStartIndex={keepStartIndex} firstOriginalPdt={firstOriginalBlock?.ProgramDateTimeUtc:O} firstOriginalEndUtc={firstOriginalBlock?.EndTimeUtc:O} firstOriginalSegment={firstOriginalBlock?.SegmentUri} lastOriginalPdt={lastOriginalBlock?.ProgramDateTimeUtc:O} lastOriginalEndUtc={lastOriginalBlock?.EndTimeUtc:O} lastOriginalSegment={lastOriginalBlock?.SegmentUri}");
            return string.Join('\n', parseResult.HeaderLines
                .Concat(segmentBlocks.SelectMany(block => block.Lines))
                .Concat(parseResult.FooterLines));
        }

        var keptBlocks = segmentBlocks.Skip(keepStartIndex).ToList();
        var headerLines = parseResult.HeaderLines.ToList();
        var mediaSequenceIndex = headerLines.FindIndex(line =>
            line.StartsWith("#EXT-X-MEDIA-SEQUENCE:", StringComparison.Ordinal));
        if (mediaSequenceIndex >= 0 &&
            int.TryParse(headerLines[mediaSequenceIndex]["#EXT-X-MEDIA-SEQUENCE:".Length..], out var mediaSequence))
        {
            headerLines[mediaSequenceIndex] = $"#EXT-X-MEDIA-SEQUENCE:{mediaSequence + keepStartIndex}";
        }

        logger.ZLogDebug(
            $"radiko live playlist trim result. mode={trimMode} thresholdUtc={threshold:O} recordingStartUtc={recordingStartUtc:O} nowUtc={nowUtc:O} originalSegments={segmentBlocks.Count} keptSegments={keptBlocks.Count} thresholdIndex={thresholdIndex} keepStartIndex={keepStartIndex} firstOriginalPdt={firstOriginalBlock?.ProgramDateTimeUtc:O} firstOriginalEndUtc={firstOriginalBlock?.EndTimeUtc:O} firstOriginalSegment={firstOriginalBlock?.SegmentUri} lastOriginalPdt={lastOriginalBlock?.ProgramDateTimeUtc:O} lastOriginalEndUtc={lastOriginalBlock?.EndTimeUtc:O} lastOriginalSegment={lastOriginalBlock?.SegmentUri} firstKeptPdt={keptBlocks.FirstOrDefault()?.ProgramDateTimeUtc:O} firstKeptEndUtc={keptBlocks.FirstOrDefault()?.EndTimeUtc:O} firstKeptSegment={keptBlocks.FirstOrDefault()?.SegmentUri} lastKeptPdt={keptBlocks.LastOrDefault()?.ProgramDateTimeUtc:O} lastKeptEndUtc={keptBlocks.LastOrDefault()?.EndTimeUtc:O} lastKeptSegment={keptBlocks.LastOrDefault()?.SegmentUri}");

        var rewrittenLines = new List<string>(headerLines.Count + keptBlocks.Sum(x => x.Lines.Count) + parseResult.FooterLines.Count);
        rewrittenLines.AddRange(headerLines);
        foreach (var block in keptBlocks)
        {
            rewrittenLines.AddRange(block.Lines);
        }

        rewrittenLines.AddRange(parseResult.FooterLines);
        return string.Join('\n', rewrittenLines);
    }

    private static LivePlaylistParseResult ParseLiveMediaPlaylist(string playlist)
    {
        var normalized = playlist.Replace("\r\n", "\n");
        var lines = normalized
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        var isMasterPlaylist = lines.Any(line => line.StartsWith("#EXT-X-STREAM-INF", StringComparison.Ordinal));
        var firstSegmentLineIndex = lines.FindIndex(line => !line.StartsWith('#'));
        if (isMasterPlaylist || firstSegmentLineIndex < 0)
        {
            return new LivePlaylistParseResult(
                normalized,
                lines,
                isMasterPlaylist,
                firstSegmentLineIndex,
                [],
                [],
                []);
        }

        var headerLines = lines
            .Take(firstSegmentLineIndex)
            .Where(line => !line.StartsWith("#EXT-X-START:", StringComparison.Ordinal))
            .ToList();

        var footerLines = new List<string>();
        var segmentBlocks = new List<LivePlaylistSegmentBlock>();
        var currentLines = new List<string>();
        DateTimeOffset? currentProgramDateTime = null;
        double? currentDurationSeconds = null;

        for (var i = firstSegmentLineIndex; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.StartsWith('#'))
            {
                if (line.StartsWith("#EXT-X-ENDLIST", StringComparison.Ordinal))
                {
                    footerLines.Add(line);
                    continue;
                }

                currentLines.Add(line);
                if (line.StartsWith("#EXT-X-PROGRAM-DATE-TIME:", StringComparison.Ordinal))
                {
                    var value = line["#EXT-X-PROGRAM-DATE-TIME:".Length..];
                    if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    {
                        currentProgramDateTime = parsed;
                    }
                }
                else if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
                {
                    var value = line["#EXTINF:".Length..];
                    var commaIndex = value.IndexOf(',');
                    if (commaIndex >= 0)
                    {
                        value = value[..commaIndex];
                    }

                    if (double.TryParse(value, CultureInfo.InvariantCulture, out var durationSeconds))
                    {
                        currentDurationSeconds = durationSeconds;
                    }
                }

                continue;
            }

            currentLines.Add(line);
            segmentBlocks.Add(new LivePlaylistSegmentBlock(
                currentLines.ToList(),
                currentProgramDateTime,
                currentDurationSeconds));
            currentLines.Clear();
            currentProgramDateTime = null;
            currentDurationSeconds = null;
        }

        return new LivePlaylistParseResult(
            normalized,
            lines,
            isMasterPlaylist,
            firstSegmentLineIndex,
            headerLines,
            footerLines,
            segmentBlocks);
    }

    private sealed record LivePlaylistSegmentBlock(
        List<string> Lines,
        DateTimeOffset? ProgramDateTimeUtc,
        double? DurationSeconds)
    {
        public string? SegmentUri => Lines.LastOrDefault(line => !line.StartsWith('#'));

        public DateTimeOffset? EndTimeUtc =>
            ProgramDateTimeUtc.HasValue && DurationSeconds.HasValue
                ? ProgramDateTimeUtc.Value.AddSeconds(DurationSeconds.Value)
                : null;
    }

    private sealed record LivePlaylistParseResult(
        string Normalized,
        List<string> Lines,
        bool IsMasterPlaylist,
        int FirstSegmentLineIndex,
        List<string> HeaderLines,
        List<string> FooterLines,
        List<LivePlaylistSegmentBlock> SegmentBlocks);
}
