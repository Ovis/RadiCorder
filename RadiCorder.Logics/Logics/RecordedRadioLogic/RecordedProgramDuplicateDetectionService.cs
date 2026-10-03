using RadiCorder.Logics.Domain.DuplicateDetection;
using RadiCorder.Logics.Infrastructure.Recording;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Domain.Recording;
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
    private readonly RecordingAudioFingerprintReader _audioReader = new(logger, config, configuration);

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
            if (!_audioReader.IsAvailable)
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
                select new DuplicateRecording
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
                    NormalizedTitle = DuplicateSimilarity.NormalizeTitle(x.Title)
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

            var phase1Groups = DuplicateCandidateSelector.BuildPhase1Groups(prepared, broadcastClusterWindowHours, logger);
            var phase1PairsCount = phase1Groups.Sum(x => x.Pairs.Count);
            logger.ZLogInformation($"類似抽出(1段目) 完了: 対象録音={prepared.Count}件, 放送回クラスタ={phase1Groups.Count}件, 候補ペア={phase1PairsCount}件");
            if (phase1PairsCount == 0)
            {
                return (true, [], null, null);
            }

            var strictMode = string.Equals(phase2Mode, "strict", StringComparison.OrdinalIgnoreCase);
            var phase2Targets = DuplicateCandidateSelector.BuildPhase2TargetsByGroup(phase1Groups, maxPhase1Groups, strictMode);
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

                var leftFingerprint = await _audioReader.GetFingerprintAsync(candidate.Left, fingerprintCache, cancellationToken);
                var rightFingerprint = await _audioReader.GetFingerprintAsync(candidate.Right, fingerprintCache, cancellationToken);

                if (leftFingerprint.Length == 0 || rightFingerprint.Length == 0)
                {
                    continue;
                }

                var audioScore = DuplicateSimilarity.CalculateBestCorrelationScore(leftFingerprint, rightFingerprint);
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

    private static RecordedDuplicateSideEntry ToSideEntry(DuplicateRecording source)
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




















}
