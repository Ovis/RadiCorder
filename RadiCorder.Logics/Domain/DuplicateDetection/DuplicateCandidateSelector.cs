using Microsoft.Extensions.Logging;
using ZLogger;

namespace RadiCorder.Logics.Domain.DuplicateDetection;

/// <summary>
/// 放送回クラスタとタイトル・長さから音声比較する候補を選択する。
/// </summary>
public static class DuplicateCandidateSelector
{
    public static List<DuplicatePair> BuildPhase1Candidates(List<DuplicateRecording> items)
    {
        var result = new List<DuplicatePair>();
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

                var titleSimilarity = DuplicateSimilarity.CalculateTitleSimilarity(left.NormalizedTitle, right.NormalizedTitle);
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

                result.Add(new DuplicatePair
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

    public static List<DuplicateGroup> BuildPhase1Groups(List<DuplicateRecording> items, int broadcastClusterWindowHours, ILogger logger)
    {
        var groups = new List<DuplicateGroup>();
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

                var group = new DuplicateGroup
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

    public static List<DuplicatePair> BuildPhase2TargetsByGroup(
        List<DuplicateGroup> phase1Groups,
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

        var compactPairs = new List<DuplicatePair>();
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

    private static List<List<DuplicateRecording>> SplitByBroadcastWindow(List<DuplicateRecording> source, int broadcastClusterWindowHours)
    {
        if (source.Count == 0)
        {
            return [];
        }

        var orderedMembers = source
            .OrderBy(x => x.StartDateTime)
            .ToList();

        var clusters = new List<List<DuplicateRecording>>();
        List<DuplicateRecording>? currentCluster = null;
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
}
