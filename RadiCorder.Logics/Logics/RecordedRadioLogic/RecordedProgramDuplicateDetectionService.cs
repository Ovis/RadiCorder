using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Logics.RecordedRadioLogic;

/// <summary>
/// 録音済み番組から類似内容の候補を抽出する
/// </summary>
public class RecordedProgramDuplicateDetectionService(
    ILogger<RecordedProgramDuplicateDetectionService> logger,
    IAppConfigurationService config,
    IConfiguration configuration,
    RadioDbContext dbContext)
{
    private const int AudioSampleRate = 8000;
    private const int MinOverlapSeconds = 120;
    private const int MaxShiftSeconds = 120;
    private static readonly Regex NonAlphaNumericRegex = new(@"[^\p{L}\p{Nd}]+", RegexOptions.Compiled);
    private static readonly Regex BracketRegex = new(@"[\(\[（【].*?[\)\]）】]", RegexOptions.Compiled);
    private static readonly Regex MultiSpaceRegex = new(@"\s+", RegexOptions.Compiled);

    private readonly string _ffmpegPath = ResolveFfmpegPath(configuration, config);

    public async ValueTask<(bool IsSuccess, List<RecordedDuplicateCandidateEntry> List, string? ErrorMessage, Exception? Error)> DetectAsync(
        int lookbackDays,
        int maxPhase1Groups,
        string phase2Mode,
        int broadcastClusterWindowHours,
        double finalThreshold,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_ffmpegPath) || !File.Exists(_ffmpegPath))
            {
                return (false, [], "ffmpeg が見つからないため、類似番組抽出を実行できません。", null);
            }

            var totalCompletedRecordings = await dbContext.Recordings
                .AsNoTracking()
                .CountAsync(x => x.State == RecordingState.Completed, cancellationToken);
            logger.ZLogInformation($"類似抽出(前処理) Completed録音件数={totalCompletedRecordings}件");

            var stateSummary = await dbContext.Recordings
                .AsNoTracking()
                .GroupBy(x => x.State)
                .Select(x => new { State = x.Key, Count = x.Count() })
                .OrderByDescending(x => x.Count)
                .ToListAsync(cancellationToken);
            if (stateSummary.Count > 0)
            {
                var states = string.Join(" | ", stateSummary.Select(x => $"{x.State}:{x.Count}"));
                logger.ZLogInformation($"類似抽出(前処理) 録音State内訳={states}");
            }

            var completedIds = await dbContext.Recordings
                .AsNoTracking()
                .Where(x => x.State == RecordingState.Completed)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            var completedIdSet = completedIds.ToHashSet();

            var metadataRecordingIds = await dbContext.RecordingMetadatas
                .AsNoTracking()
                .Where(x => completedIdSet.Contains(x.RecordingId))
                .Select(x => x.RecordingId)
                .Distinct()
                .ToListAsync(cancellationToken);
            var metadataIdSet = metadataRecordingIds.ToHashSet();

            var fileRecordingIds = await dbContext.RecordingFiles
                .AsNoTracking()
                .Where(x => completedIdSet.Contains(x.RecordingId))
                .Select(x => x.RecordingId)
                .Distinct()
                .ToListAsync(cancellationToken);
            var fileIdSet = fileRecordingIds.ToHashSet();

            var completedWithMetadataCount = completedIds.Count(x => metadataIdSet.Contains(x));
            var completedWithFileCount = completedIds.Count(x => fileIdSet.Contains(x));
            var completedWithBothCount = completedIds.Count(x => metadataIdSet.Contains(x) && fileIdSet.Contains(x));
            logger.ZLogInformation(
                $"類似抽出(前処理) Completed内訳 Metadata有={completedWithMetadataCount}件 / File有={completedWithFileCount}件 / 両方有={completedWithBothCount}件");

            var baseQuery =
                from r in dbContext.Recordings.AsNoTracking()
                join m in dbContext.RecordingMetadatas.AsNoTracking() on r.Id equals m.RecordingId
                join f in dbContext.RecordingFiles.AsNoTracking() on r.Id equals f.RecordingId
                where r.State == RecordingState.Completed
                select new RawRecording
                {
                    RecordingId = r.Id,
                    StationId = r.StationId,
                    StationName = m.StationName,
                    Title = m.Title,
                    StartDateTime = r.StartDateTime,
                    EndDateTime = r.EndDateTime,
                    FileRelativePath = f.FileRelativePath
                };

            if (lookbackDays > 0)
            {
                var fromTime = DateTimeOffset.UtcNow.AddDays(-lookbackDays);
                baseQuery = baseQuery.Where(x => x.EndDateTime >= fromTime);
            }

            var rows = await baseQuery
                .ToListAsync(cancellationToken);
            logger.ZLogInformation($"類似抽出(前処理) JOIN後対象件数={rows.Count}件 lookbackDays={lookbackDays}");

            var joinedIdSet = rows
                .Select(x => x.RecordingId)
                .Distinct()
                .ToHashSet();
            var missingByJoinCount = completedIds.Count(x => !joinedIdSet.Contains(x));
            logger.ZLogInformation($"類似抽出(前処理) JOIN除外件数={missingByJoinCount}件");

            if (rows.Count < 2)
            {
                return (true, [], null, null);
            }

            var prepared = rows
                .Select(x => x with
                {
                    DurationSeconds = Math.Max((x.EndDateTime - x.StartDateTime).TotalSeconds, 1d),
                    NormalizedTitle = NormalizeTitle(x.Title)
                })
                .Where(x => x.DurationSeconds >= 60)
                .ToList();
            logger.ZLogInformation($"類似抽出(前処理) 60秒以上対象件数={prepared.Count}件");
            var droppedByDuration = rows.Count - prepared.Count;
            logger.ZLogInformation($"類似抽出(前処理) 60秒未満除外件数={droppedByDuration}件");

            var topTitles = prepared
                .GroupBy(x => string.IsNullOrWhiteSpace(x.NormalizedTitle) ? x.Title : x.NormalizedTitle)
                .Select(x => new { Title = x.Key, Count = x.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToList();
            if (topTitles.Count > 0)
            {
                var titleSummary = string.Join(" | ", topTitles.Select(x => $"{x.Title}:{x.Count}"));
                logger.ZLogInformation($"類似抽出(前処理) 上位タイトル件数={titleSummary}");
            }

            var phase1Groups = BuildPhase1Groups(prepared, broadcastClusterWindowHours);
            var phase1PairsCount = phase1Groups.Sum(x => x.Pairs.Count);
            logger.ZLogInformation($"類似抽出(1段目) 完了: 対象録音={prepared.Count}件, 放送回クラスタ={phase1Groups.Count}件, 候補ペア={phase1PairsCount}件");
            if (phase1PairsCount == 0)
            {
                return (true, [], null, null);
            }

            var strictMode = string.Equals(phase2Mode, "strict", StringComparison.OrdinalIgnoreCase);
            var phase2Targets = BuildPhase2TargetsByGroup(phase1Groups, maxPhase1Groups, strictMode);
            logger.ZLogInformation($"類似抽出(2段目対象) グループ上限={maxPhase1Groups} / モード={phase2Mode} / 時間窓={broadcastClusterWindowHours}h / 対象ペア={phase2Targets.Count}件");
            if (phase2Targets.Count == 0)
            {
                return (true, [], null, null);
            }

            var fingerprintCache = new Dictionary<Ulid, double[]>();
            var result = new List<RecordedDuplicateCandidateEntry>();

            for (var index = 0; index < phase2Targets.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = phase2Targets[index];

                var leftFingerprint = await GetFingerprintAsync(candidate.Left, fingerprintCache, cancellationToken);
                var rightFingerprint = await GetFingerprintAsync(candidate.Right, fingerprintCache, cancellationToken);

                if (leftFingerprint.Length == 0 || rightFingerprint.Length == 0)
                {
                    continue;
                }

                var audioScore = CalculateBestCorrelationScore(leftFingerprint, rightFingerprint);
                var finalScore = (candidate.Phase1Score * 0.45) + (audioScore * 0.55);
                if (finalScore < finalThreshold)
                {
                    continue;
                }

                result.Add(new RecordedDuplicateCandidateEntry
                {
                    Left = ToSideEntry(candidate.Left),
                    Right = ToSideEntry(candidate.Right),
                    Phase1Score = Math.Round(candidate.Phase1Score, 3),
                    AudioScore = Math.Round(audioScore, 3),
                    FinalScore = Math.Round(finalScore, 3),
                    StartTimeDiffHours = Math.Round(Math.Abs((candidate.Left.StartDateTime - candidate.Right.StartDateTime).TotalHours), 2),
                    DurationDiffSeconds = Math.Round(Math.Abs(candidate.Left.DurationSeconds - candidate.Right.DurationSeconds), 1)
                });

                if ((index + 1) % 10 == 0 || index + 1 == phase2Targets.Count)
                {
                    logger.ZLogInformation($"類似抽出(2段目) 進捗: {index + 1}/{phase2Targets.Count}件 処理, 一致候補={result.Count}件");
                }
            }

            return (true, result.OrderByDescending(x => x.FinalScore).ToList(), null, null);
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"類似録音候補の抽出に失敗しました。");
            return (false, [], "類似録音候補の抽出に失敗しました。", ex);
        }
    }

    private static RecordedDuplicateSideEntry ToSideEntry(RawRecording source)
    {
        return new RecordedDuplicateSideEntry
        {
            RecordingId = source.RecordingId.ToString(),
            Title = source.Title,
            StationId = source.StationId,
            StationName = source.StationName,
            StartDateTime = source.StartDateTime,
            EndDateTime = source.EndDateTime,
            DurationSeconds = Math.Round(source.DurationSeconds, 1)
        };
    }

    private List<Phase1Candidate> BuildPhase1Candidates(List<RawRecording> items)
    {
        var result = new List<Phase1Candidate>();
        for (var i = 0; i < items.Count; i++)
        {
            for (var j = i + 1; j < items.Count; j++)
            {
                var left = items[i];
                var right = items[j];

                if (left.RecordingId == right.RecordingId)
                {
                    continue;
                }

                if (string.Equals(left.StationId, right.StationId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var diffDays = Math.Abs((left.StartDateTime - right.StartDateTime).TotalDays);
                if (diffDays > 7d)
                {
                    continue;
                }

                var maxDuration = Math.Max(left.DurationSeconds, right.DurationSeconds);
                var minDuration = Math.Min(left.DurationSeconds, right.DurationSeconds);
                var durationRatio = minDuration / maxDuration;
                if (durationRatio < 0.7d)
                {
                    continue;
                }

                var titleSimilarity = CalculateTitleSimilarity(left.NormalizedTitle, right.NormalizedTitle);
                if (titleSimilarity < 0.58d)
                {
                    continue;
                }

                var timeCloseness = 1d - Math.Min(diffDays / 7d, 1d);
                var phase1Score = (titleSimilarity * 0.55d) + (durationRatio * 0.25d) + (timeCloseness * 0.20d);
                if (phase1Score < 0.62d)
                {
                    continue;
                }

                result.Add(new Phase1Candidate
                {
                    Left = left,
                    Right = right,
                    Phase1Score = phase1Score
                });
            }
        }

        return result
            .OrderByDescending(x => x.Phase1Score)
            .ToList();
    }

    private List<Phase1Group> BuildPhase1Groups(List<RawRecording> items, int broadcastClusterWindowHours)
    {
        var groups = new List<Phase1Group>();
        var titleGroups = items
            .GroupBy(x => string.IsNullOrWhiteSpace(x.NormalizedTitle) ? x.Title : x.NormalizedTitle)
            .ToList();
        logger.ZLogInformation($"類似抽出(1段目) タイトルグループ数={titleGroups.Count}件 時間窓={broadcastClusterWindowHours}h");

        foreach (var titleGroup in titleGroups)
        {
            var clusters = SplitByBroadcastWindow(titleGroup.ToList(), broadcastClusterWindowHours);
            logger.ZLogDebug($"類似抽出(1段目) タイトル={titleGroup.Key} 録音={titleGroup.Count()}件 放送回クラスタ候補={clusters.Count}件");
            foreach (var cluster in clusters)
            {
                if (cluster.Count < 2)
                {
                    continue;
                }

                var pairs = BuildPhase1Candidates(cluster);
                if (pairs.Count == 0)
                {
                    continue;
                }

                var group = new Phase1Group
                {
                    MaxPhase1Score = pairs.Max(x => x.Phase1Score)
                };
                foreach (var pair in pairs)
                {
                    group.Pairs.Add(pair);
                    group.MemberIds.Add(pair.Left.RecordingId);
                    group.MemberIds.Add(pair.Right.RecordingId);
                }
                groups.Add(group);
            }
        }

        var topClusterSizes = groups
            .Select(x => x.MemberIds.Count)
            .OrderByDescending(x => x)
            .Take(10)
            .ToList();
        if (topClusterSizes.Count > 0)
        {
            logger.ZLogInformation($"類似抽出(1段目) クラスタサイズ上位={string.Join(",", topClusterSizes)}");
        }

        return groups;
    }

    private static List<Phase1Candidate> BuildPhase2TargetsByGroup(
        List<Phase1Group> phase1Groups,
        int maxPhase1Groups,
        bool strictMode)
    {
        var selectedGroups = phase1Groups
            .OrderByDescending(x => x.MaxPhase1Score)
            .ThenByDescending(x => x.Pairs.Count)
            .ThenByDescending(x => x.MemberIds.Count)
            .Take(maxPhase1Groups)
            .ToList();

        if (strictMode)
        {
            return selectedGroups
                .SelectMany(x => x.Pairs)
                .OrderByDescending(x => x.Phase1Score)
                .ToList();
        }

        var compactPairs = new List<Phase1Candidate>();
        foreach (var group in selectedGroups)
        {
            var memberScore = new Dictionary<Ulid, double>();
            foreach (var pair in group.Pairs)
            {
                memberScore[pair.Left.RecordingId] = memberScore.GetValueOrDefault(pair.Left.RecordingId, 0d) + pair.Phase1Score;
                memberScore[pair.Right.RecordingId] = memberScore.GetValueOrDefault(pair.Right.RecordingId, 0d) + pair.Phase1Score;
            }

            var representativeId = memberScore
                .OrderByDescending(x => x.Value)
                .Select(x => x.Key)
                .FirstOrDefault();

            var representativePairs = group.Pairs
                .Where(x => x.Left.RecordingId == representativeId || x.Right.RecordingId == representativeId)
                .OrderByDescending(x => x.Phase1Score)
                .ToList();

            if (representativePairs.Count == 0 && group.Pairs.Count > 0)
            {
                representativePairs.Add(group.Pairs[0]);
            }

            compactPairs.AddRange(representativePairs);
        }

        return compactPairs
            .OrderByDescending(x => x.Phase1Score)
            .ToList();
    }

    private static List<List<RawRecording>> SplitByBroadcastWindow(List<RawRecording> source, int broadcastClusterWindowHours)
    {
        if (source.Count == 0)
        {
            return [];
        }

        var orderedMembers = source
            .OrderBy(x => x.StartDateTime)
            .ToList();

        var clusters = new List<List<RawRecording>>();
        List<RawRecording>? currentCluster = null;
        DateTimeOffset clusterAnchor = default;

        foreach (var member in orderedMembers)
        {
            if (currentCluster == null)
            {
                currentCluster = [];
                currentCluster.Add(member);
                clusters.Add(currentCluster);
                clusterAnchor = member.StartDateTime;
                continue;
            }

            var diffHours = Math.Abs((member.StartDateTime - clusterAnchor).TotalHours);
            // 境界値ちょうど(例: 168h)は次放送回として分離する
            if (diffHours < broadcastClusterWindowHours)
            {
                currentCluster.Add(member);
                continue;
            }

            currentCluster = [];
            currentCluster.Add(member);
            clusters.Add(currentCluster);
            clusterAnchor = member.StartDateTime;
        }

        return clusters;
    }

    private async ValueTask<double[]> GetFingerprintAsync(
        RawRecording recording,
        IDictionary<Ulid, double[]> cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(recording.RecordingId, out var cached))
        {
            return cached;
        }

        var path = ResolveRecordingPath(recording);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            logger.ZLogWarning($"類似抽出: 録音ファイルが見つかりません。recordingId={recording.RecordingId} path={path}");
            cache[recording.RecordingId] = [];
            return [];
        }

        var sampleSeconds = (int)Math.Clamp(recording.DurationSeconds * 0.6d, 180d, 900d);
        sampleSeconds = Math.Min(sampleSeconds, (int)Math.Max(recording.DurationSeconds - 2d, 30d));
        var startSeconds = Math.Max(0d, (recording.DurationSeconds - sampleSeconds) / 2d);

        var args =
            $"-hide_banner -loglevel error -nostdin -ss {startSeconds.ToString("0.###", CultureInfo.InvariantCulture)} -i \"{path}\" -t {sampleSeconds.ToString(CultureInfo.InvariantCulture)} -vn -ac 1 -ar {AudioSampleRate} -f s16le -";

        try
        {
            var (exitCode, stdout, stderr) = await ExecuteProcessAsync(_ffmpegPath, args, cancellationToken);
            if (exitCode != 0 || stdout.Length == 0)
            {
                logger.ZLogWarning($"類似抽出: ffmpeg 解析失敗 recordingId={recording.RecordingId} exitCode={exitCode} stderr={stderr}");
                cache[recording.RecordingId] = [];
                return [];
            }

            var bins = BuildEnergyBins(stdout);
            cache[recording.RecordingId] = bins;
            return bins;
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"類似抽出: 音声指紋化に失敗 recordingId={recording.RecordingId}");
            cache[recording.RecordingId] = [];
            return [];
        }
    }

    private string ResolveRecordingPath(RawRecording recording)
    {
        if (config.RecordFileSaveDir.TryCombinePaths(recording.FileRelativePath, out var fullPath))
        {
            return fullPath;
        }

        return string.Empty;
    }

    private static double[] BuildEnergyBins(byte[] pcmBytes)
    {
        if (pcmBytes.Length < 2)
        {
            return [];
        }

        var sampleCount = pcmBytes.Length / 2;
        if (sampleCount < AudioSampleRate)
        {
            return [];
        }

        var seconds = sampleCount / AudioSampleRate;
        var bins = new double[seconds];

        for (var sec = 0; sec < seconds; sec++)
        {
            long sum = 0;
            var offset = sec * AudioSampleRate * 2;
            for (var i = 0; i < AudioSampleRate; i++)
            {
                var index = offset + (i * 2);
                var value = BitConverter.ToInt16(pcmBytes, index);
                // short.MinValue (-32768) に対する Math.Abs(short) の OverflowException を回避する
                sum += Math.Abs((int)value);
            }

            bins[sec] = sum / (double)AudioSampleRate;
        }

        SmoothInPlace(bins);
        NormalizeInPlace(bins);
        return bins;
    }

    private static void SmoothInPlace(double[] values)
    {
        if (values.Length < 5)
        {
            return;
        }

        var source = values.ToArray();
        for (var i = 0; i < values.Length; i++)
        {
            var from = Math.Max(0, i - 2);
            var to = Math.Min(values.Length - 1, i + 2);
            var sum = 0d;
            for (var j = from; j <= to; j++)
            {
                sum += source[j];
            }

            values[i] = sum / (to - from + 1);
        }
    }

    private static void NormalizeInPlace(double[] values)
    {
        if (values.Length == 0)
        {
            return;
        }

        var mean = values.Average();
        var variance = values.Sum(v => Math.Pow(v - mean, 2d)) / values.Length;
        var std = Math.Sqrt(variance);
        if (std < 1e-9)
        {
            return;
        }

        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (values[i] - mean) / std;
        }
    }

    private static double CalculateBestCorrelationScore(double[] left, double[] right)
    {
        if (left.Length < MinOverlapSeconds || right.Length < MinOverlapSeconds)
        {
            return 0d;
        }

        var best = double.MinValue;
        for (var shift = -MaxShiftSeconds; shift <= MaxShiftSeconds; shift++)
        {
            var correlation = CalculatePearsonWithShift(left, right, shift, out var overlap);
            if (overlap < MinOverlapSeconds)
            {
                continue;
            }

            if (correlation > best)
            {
                best = correlation;
            }
        }

        if (double.IsNegativeInfinity(best) || best == double.MinValue)
        {
            return 0d;
        }

        return Math.Clamp((best + 1d) / 2d, 0d, 1d);
    }

    private static double CalculatePearsonWithShift(double[] left, double[] right, int shift, out int overlap)
    {
        var leftStart = Math.Max(0, shift);
        var rightStart = Math.Max(0, -shift);
        overlap = Math.Min(left.Length - leftStart, right.Length - rightStart);
        if (overlap <= 1)
        {
            return 0d;
        }

        var leftSpan = left.AsSpan(leftStart, overlap);
        var rightSpan = right.AsSpan(rightStart, overlap);

        var meanLeft = 0d;
        var meanRight = 0d;
        for (var i = 0; i < overlap; i++)
        {
            meanLeft += leftSpan[i];
            meanRight += rightSpan[i];
        }

        meanLeft /= overlap;
        meanRight /= overlap;

        var numerator = 0d;
        var denLeft = 0d;
        var denRight = 0d;
        for (var i = 0; i < overlap; i++)
        {
            var l = leftSpan[i] - meanLeft;
            var r = rightSpan[i] - meanRight;
            numerator += l * r;
            denLeft += l * l;
            denRight += r * r;
        }

        var denominator = Math.Sqrt(denLeft * denRight);
        if (denominator < 1e-9)
        {
            return 0d;
        }

        return numerator / denominator;
    }

    private static string NormalizeTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var value = raw.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        value = BracketRegex.Replace(value, " ");
        value = MultiSpaceRegex.Replace(value, " ").Trim();
        value = NonAlphaNumericRegex.Replace(value, string.Empty);
        return value;
    }

    private static double CalculateTitleSimilarity(string left, string right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return 0d;
        }

        if (left == right)
        {
            return 1d;
        }

        var distance = LevenshteinDistance(left, right);
        var maxLen = Math.Max(left.Length, right.Length);
        if (maxLen == 0)
        {
            return 1d;
        }

        return 1d - (distance / (double)maxLen);
    }

    private static int LevenshteinDistance(string source, string target)
    {
        var m = source.Length;
        var n = target.Length;
        var d = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= n; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[m, n];
    }

    private static async ValueTask<(int ExitCode, byte[] StdOut, string StdErr)> ExecuteProcessAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var stdOutTask = ReadAllBytesAsync(process.StandardOutput.BaseStream, cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(120));

        await process.WaitForExitAsync(timeoutCts.Token);
        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        return (process.ExitCode, stdOut, stdErr);
    }

    private static async ValueTask<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        await using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        return ms.ToArray();
    }

    private static string ResolveFfmpegPath(IConfiguration configuration, IAppConfigurationService config)
    {
        var configuredPath = config.FfmpegExecutablePath;
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var bundledCandidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe")
        };

        foreach (var candidate in bundledCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var fromPath = FindByCommand("where", "ffmpeg.exe");
            if (!string.IsNullOrWhiteSpace(fromPath))
            {
                return fromPath;
            }
        }
        else
        {
            var fromPath = FindByCommand("which", "ffmpeg");
            if (!string.IsNullOrWhiteSpace(fromPath))
            {
                return fromPath;
            }
        }

        var fallback = configuration["RadiCorder:FfmpegExecutablePath"];
        return fallback ?? string.Empty;
    }

    private static string FindByCommand(string command, string args)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadLine();
            process.WaitForExit();
            return output ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed record RawRecording
    {
        public Ulid RecordingId { get; init; }
        public string StationId { get; init; } = string.Empty;
        public string StationName { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string NormalizedTitle { get; init; } = string.Empty;
        public DateTimeOffset StartDateTime { get; init; }
        public DateTimeOffset EndDateTime { get; init; }
        public double DurationSeconds { get; init; }
        public string FileRelativePath { get; init; } = string.Empty;
    }

    private sealed class Phase1Candidate
    {
        public required RawRecording Left { get; init; }
        public required RawRecording Right { get; init; }
        public required double Phase1Score { get; init; }
    }

    private sealed class Phase1Group
    {
        public HashSet<Ulid> MemberIds { get; } = [];
        public List<Phase1Candidate> Pairs { get; } = [];
        public double MaxPhase1Score { get; set; }
    }
}
