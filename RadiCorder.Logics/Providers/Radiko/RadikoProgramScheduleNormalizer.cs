using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Providers.Radiko;

/// <summary>
/// 週間番組表の不完全な項目を補正し、取り込める番組と診断情報を返す。
/// </summary>
public static class RadikoProgramScheduleNormalizer
{
    public static (List<RadikoProgram> Programs, List<RadikoProgramScheduleWarning> Warnings) Normalize(
        IReadOnlyList<RadikoProgram> programs)
    {
        var result = new List<RadikoProgram>();
        var warnings = new List<RadikoProgramScheduleWarning>();
        var startsByStation = programs
            .Where(p => p.EndTime >= p.StartTime)
            .GroupBy(p => p.StationId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.StartTime).Distinct().Order().ToArray());

        foreach (var program in programs)
        {
            if (program.EndTime < program.StartTime)
            {
                warnings.Add(new(program.ProgramId, "終了日時が開始日時より前のためスキップ", program.StartTime, program.EndTime));
                continue;
            }

            if (program.EndTime == program.StartTime)
            {
                var starts = startsByStation[program.StationId];
                var nextIndex = Array.BinarySearch(starts, program.StartTime) + 1;
                // 同じ局の次の開始時刻を根拠にする。後続番組がなければ推測しない。
                if (nextIndex >= starts.Length)
                {
                    warnings.Add(new(program.ProgramId, "長さ0秒で後続番組がないためスキップ", program.StartTime, program.EndTime));
                    continue;
                }

                var estimatedEnd = starts[nextIndex];
                warnings.Add(new(program.ProgramId, "長さ0秒の終了日時を後続番組の開始日時から推定", program.StartTime, program.EndTime, estimatedEnd));
                program.EndTime = estimatedEnd;
            }

            if (string.IsNullOrWhiteSpace(program.Title))
            {
                warnings.Add(new(program.ProgramId, "番組名が空欄のまま登録", program.StartTime, program.EndTime));
            }
            result.Add(program);
        }

        return (result, warnings);
    }
}

/// <summary>
/// 元の番組情報と、補正またはスキップの理由を保持する。
/// </summary>
public sealed record RadikoProgramScheduleWarning(
    string ProgramId, string Reason, DateTimeOffset StartTime, DateTimeOffset EndTime, DateTimeOffset? EstimatedEndTime = null);
