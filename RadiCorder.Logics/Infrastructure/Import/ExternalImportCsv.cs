using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using RadiCorder.Logics.Models.ExternalImport;
using static RadiCorder.Logics.Infrastructure.Import.ExternalImportDefaults;

namespace RadiCorder.Logics.Infrastructure.Import;

/// <summary>
/// 外部取込候補のCSV入出力を担当する。
/// </summary>
public static class ExternalImportCsv
{
    /// <summary>
    /// 候補をCSVとして出力する
    /// </summary>
    public static byte[] ExportCandidatesCsv(IReadOnlyList<ExternalImportCandidateEntry> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\"FilePath\",\"Title\",\"Description\",\"StationName\",\"BroadcastAt\",\"Tags\"");

        foreach (var candidate in candidates)
        {
            var tags = string.Join("|", candidate.Tags.Select(t => EscapeCsvFormula(t.Trim())));
            var line = string.Join(",",
                CsvEscape(candidate.FilePath),
                CsvEscape(EscapeCsvFormula(candidate.Title)),
                CsvEscape(EscapeCsvFormula(candidate.Description)),
                CsvEscape(EscapeCsvFormula(candidate.StationName)),
                CsvEscape(candidate.BroadcastAt.ToString("o", CultureInfo.InvariantCulture)),
                CsvEscape(tags));
            sb.AppendLine(line);
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// CSVを読み込んで候補一覧を再構築する
    /// </summary>
    public static ValueTask<(bool IsSuccess, List<ExternalImportCandidateEntry> Candidates, List<string> Errors)> ImportCandidatesCsvAsync(
        Stream stream,
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null
        };
        using var csv = new CsvReader(reader, csvConfig);

        var errors = new List<string>();
        var candidates = new List<ExternalImportCandidateEntry>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expected = new[] { "FilePath", "Title", "Description", "StationName", "BroadcastAt", "Tags" };

        try
        {
            if (!csv.Read())
            {
                return ValueTask.FromResult((false, candidates, new List<string> { "CSVが空です。" }));
            }

            csv.ReadHeader();
            var headers = csv.HeaderRecord ?? Array.Empty<string>();
            if (headers.Length != expected.Length || headers.Where((value, index) => !value.Equals(expected[index], StringComparison.Ordinal)).Any())
            {
                return ValueTask.FromResult((false, candidates, new List<string> { "CSVヘッダーが不正です。" }));
            }
        }
        catch
        {
            return ValueTask.FromResult((false, candidates, new List<string> { "CSVヘッダーが不正です。" }));
        }

        try
        {
            while (csv.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (candidates.Count >= CsvMaxRows)
                {
                    errors.Add($"CSV行数が上限を超えています。上限: {CsvMaxRows} 行");
                    return ValueTask.FromResult((false, candidates, errors));
                }

                var rowNumber = csv.Parser.Row;
                string filePath;
                string title;
                string description;
                string stationName;
                string broadcastAtText;
                string tagsText;
                try
                {
                    filePath = csv.GetField("FilePath")?.Trim() ?? string.Empty;
                    title = csv.GetField("Title")?.Trim() ?? string.Empty;
                    description = csv.GetField("Description")?.Trim() ?? string.Empty;
                    stationName = csv.GetField("StationName")?.Trim() ?? string.Empty;
                    broadcastAtText = csv.GetField("BroadcastAt")?.Trim() ?? string.Empty;
                    tagsText = csv.GetField("Tags")?.Trim() ?? string.Empty;
                }
                catch
                {
                    errors.Add($"{rowNumber}行目: 列数が不正です。");
                    continue;
                }

                if (!DateTimeOffset.TryParse(broadcastAtText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var broadcastAt))
                {
                    errors.Add($"{rowNumber}行目: 放送日時の形式が不正です。");
                    continue;
                }

                if (!ExternalImportPaths.TryResolveManagedFilePath(filePath, rootPath, out var normalizedPath, out var normalizedRelativePath))
                {
                    errors.Add($"{rowNumber}行目: ファイルパスが不正です。");
                    continue;
                }

                if (!seenPaths.Add(normalizedPath))
                {
                    errors.Add($"{rowNumber}行目: 同じファイルパスが重複しています。");
                    continue;
                }

                var ext = Path.GetExtension(normalizedPath);
                if (!AllowedExtensions.Contains(ext))
                {
                    errors.Add($"{rowNumber}行目: 対応していない拡張子です。");
                    continue;
                }

                if (!File.Exists(normalizedPath))
                {
                    errors.Add($"{rowNumber}行目: ファイルが存在しません。");
                    continue;
                }

                var tags = tagsText
                    .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (tags.Count == 0)
                {
                    tags.Add(DefaultTagName);
                }

                candidates.Add(new ExternalImportCandidateEntry
                {
                    IsSelected = true,
                    FilePath = normalizedRelativePath,
                    Title = title,
                    Description = description,
                    StationName = string.IsNullOrWhiteSpace(stationName) ? DefaultStationName : stationName,
                    BroadcastAt = broadcastAt,
                    Tags = tags
                });
            }
        }
        catch (CsvHelperException)
        {
            errors.Add("CSVの解析に失敗しました。形式を確認してください。");
        }

        return ValueTask.FromResult((errors.Count == 0, candidates, errors));
    }

    private static string CsvEscape(string value)
    {
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string EscapeCsvFormula(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var first = value[0];
        if (first is '=' or '+' or '-' or '@')
        {
            return "'" + value;
        }

        return value;
    }
}
