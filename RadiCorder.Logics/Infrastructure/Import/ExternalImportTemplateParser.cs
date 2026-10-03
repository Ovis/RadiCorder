using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Infrastructure.Import;

/// <summary>
/// 保存先テンプレートから放送局名、タイトル、放送日時を補完する。
/// </summary>
public class ExternalImportTemplateParser(ILogger logger, IAppConfigurationService config)
{
    private static readonly string[] ParseTokens =
    [
        "$StationName$",
        "$Title$",
        "$SYYYY$",
        "$SYY$",
        "$SMM$",
        "$SM$",
        "$SDD$",
        "$SD$",
        "$STHH$",
        "$STH$",
        "$STMM$",
        "$STM$",
        "$STSS$",
        "$STS$"
    ];


    public bool TryEnrichFromTemplates(
        string relativePath,
        out string stationName,
        out string title,
        out DateTimeOffset? broadcastAt)
    {
        stationName = string.Empty;
        title = string.Empty;
        broadcastAt = null;

        var captured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hasDirectoryTemplate = false;
        var hasFileTemplate = false;
        var matchedDirectory = false;
        var matchedFile = false;

        var directoryTemplate = NormalizePathForTemplate(config.RecordDirectoryRelativePath ?? string.Empty).Trim('/');
        var fileTemplate = (config.RecordFileNameTemplate ?? string.Empty).Trim();

        var normalizedRelativePath = NormalizePathForTemplate(relativePath).Trim('/');
        var lastSeparatorIndex = normalizedRelativePath.LastIndexOf('/');
        var relativeDirectory = lastSeparatorIndex >= 0
            ? normalizedRelativePath[..lastSeparatorIndex]
            : string.Empty;
        var relativeFileNameWithExtension = lastSeparatorIndex >= 0
            ? normalizedRelativePath[(lastSeparatorIndex + 1)..]
            : normalizedRelativePath;
        var relativeFileName = Path.GetFileNameWithoutExtension(relativeFileNameWithExtension);

        if (!string.IsNullOrEmpty(directoryTemplate))
        {
            hasDirectoryTemplate = true;
            matchedDirectory = TryMatchTemplate(directoryTemplate, relativeDirectory, captured);

            // テンプレート全体が一致しない場合でも、ディレクトリ階層が部分的に一致する箇所から
            // 放送局名/タイトルの取りこぼしを防ぐ。
            TryCaptureDirectoryTokensBySegment(directoryTemplate, relativeDirectory, captured);
        }

        if (!string.IsNullOrEmpty(fileTemplate))
        {
            hasFileTemplate = true;
            matchedFile = TryMatchTemplate(fileTemplate, relativeFileName, captured);
        }

        if (!hasDirectoryTemplate && !hasFileTemplate)
        {
            logger.ZLogDebug($"外部取込テンプレートが未設定のため補完をスキップ: relativePath={relativePath}");
            return false;
        }

        if (!matchedDirectory && !matchedFile)
        {
            // 先頭セグメント補完などで token を回収できている場合は、
            // テンプレート完全一致でなくても補完結果を採用する。
            if (captured.Count > 0)
            {
                logger.ZLogDebug(
                    $"外部取込テンプレート部分一致で補完を継続: relativePath={relativePath}, captured={FormatCaptured(captured)}");
            }
            else
            {
                logger.ZLogDebug(
                    $"外部取込テンプレート不一致: relativePath={relativePath}, relativeDirectory={relativeDirectory}, relativeFileName={relativeFileName}, dirTemplate={directoryTemplate}, fileTemplate={fileTemplate}, captured={FormatCaptured(captured)}");
                return false;
            }
        }

        if (captured.TryGetValue("StationName", out var parsedStationName))
        {
            stationName = parsedStationName;
        }

        if (captured.TryGetValue("Title", out var parsedTitle))
        {
            title = parsedTitle;
        }

        broadcastAt = TryBuildBroadcastAt(captured);
        if (!broadcastAt.HasValue && TryParseBroadcastAtFromFileName(relativeFileName, out var parsedBroadcastAt, out var parsedTitleFromFileName))
        {
            broadcastAt = parsedBroadcastAt;
            if (string.IsNullOrWhiteSpace(title))
            {
                title = parsedTitleFromFileName;
            }
        }

        logger.ZLogDebug(
            $"外部取込テンプレート解析結果: relativePath={relativePath}, matchedDirectory={matchedDirectory}, matchedFile={matchedFile}, station={stationName}, title={title}, broadcastAt={broadcastAt}, captured={FormatCaptured(captured)}");

        return true;
    }

    private static string FormatCaptured(IReadOnlyDictionary<string, string> captured)
    {
        if (captured.Count == 0)
        {
            return "{}";
        }

        return "{" + string.Join(", ", captured.Select(kv => $"{kv.Key}={kv.Value}")) + "}";
    }

    private static void TryCaptureDirectoryTokensBySegment(
        string directoryTemplate,
        string relativeDirectory,
        IDictionary<string, string> captured)
    {
        var templateSegments = directoryTemplate
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var directorySegments = relativeDirectory
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (templateSegments.Length == 0 || directorySegments.Length == 0)
        {
            return;
        }

        var segmentCount = Math.Min(templateSegments.Length, directorySegments.Length);
        for (var i = 0; i < segmentCount; i++)
        {
            var templateSegment = templateSegments[i];
            var directorySegment = directorySegments[i];
            if (string.IsNullOrWhiteSpace(directorySegment))
            {
                continue;
            }

            if (templateSegment.Equals("$StationName$", StringComparison.Ordinal) &&
                !captured.ContainsKey("StationName"))
            {
                captured["StationName"] = directorySegment;
                continue;
            }

            if (templateSegment.Equals("$Title$", StringComparison.Ordinal) &&
                !captured.ContainsKey("Title"))
            {
                captured["Title"] = directorySegment;
            }
        }
    }

    private static bool TryMatchTemplate(string template, string value, IDictionary<string, string> captured)
    {
        if (string.IsNullOrEmpty(template))
        {
            return true;
        }

        var patternBuilder = new StringBuilder("^");
        var keys = new List<string>();
        var index = 0;
        var groupIndex = 0;

        while (index < template.Length)
        {
            var nextToken = ParseTokens
                .Select(token => new { Token = token, Position = template.IndexOf(token, index, StringComparison.Ordinal) })
                .Where(x => x.Position >= 0)
                .OrderBy(x => x.Position)
                .FirstOrDefault();

            if (nextToken == null)
            {
                patternBuilder.Append(Regex.Escape(template[index..]));
                break;
            }

            if (nextToken.Position > index)
            {
                patternBuilder.Append(Regex.Escape(template[index..nextToken.Position]));
            }

            var key = nextToken.Token.Trim('$');
            keys.Add(key);
            groupIndex++;
            patternBuilder.Append($"(?<g{groupIndex}>{TokenRegex(key)})");
            index = nextToken.Position + nextToken.Token.Length;
        }

        patternBuilder.Append("$");
        var regex = new Regex(patternBuilder.ToString(), RegexOptions.CultureInvariant);
        var match = regex.Match(value);
        if (!match.Success)
        {
            return false;
        }

        for (var i = 0; i < keys.Count; i++)
        {
            var groupValue = match.Groups[$"g{i + 1}"].Value;
            if (string.IsNullOrEmpty(groupValue))
            {
                continue;
            }

            if (!captured.ContainsKey(keys[i]))
            {
                captured[keys[i]] = groupValue;
            }
        }

        return true;
    }

    private static string TokenRegex(string key)
    {
        return key switch
        {
            "SYYYY" => @"\d{4}",
            "SYY" => @"\d{2}",
            "SMM" => @"\d{2}",
            "SM" => @"\d{1,2}",
            "SDD" => @"\d{2}",
            "SD" => @"\d{1,2}",
            "STHH" => @"\d{2}",
            "STH" => @"\d{1,2}",
            "STMM" => @"\d{2}",
            "STM" => @"\d{1,2}",
            "STSS" => @"\d{2}",
            "STS" => @"\d{1,2}",
            _ => @".+?"
        };
    }

    private static DateTimeOffset? TryBuildBroadcastAt(IReadOnlyDictionary<string, string> captured)
    {
        if (!TryReadNumber(captured, "SYYYY", "SYY", out var year) ||
            !TryReadNumber(captured, "SMM", "SM", out var month) ||
            !TryReadNumber(captured, "SDD", "SD", out var day))
        {
            return null;
        }

        if (!TryReadNumber(captured, "STHH", "STH", out var hour))
        {
            hour = 0;
        }

        if (!TryReadNumber(captured, "STMM", "STM", out var minute))
        {
            minute = 0;
        }

        if (!TryReadNumber(captured, "STSS", "STS", out var second))
        {
            second = 0;
        }

        if (year < 1900 || month is < 1 or > 12 || day is < 1 or > 31 ||
            hour is < 0 or > 23 || minute is < 0 or > 59 || second is < 0 or > 59)
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromHours(9));
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadNumber(IReadOnlyDictionary<string, string> captured, string primaryKey, string secondaryKey, out int value)
    {
        value = 0;
        if (captured.TryGetValue(primaryKey, out var primary) && int.TryParse(primary, out value))
        {
            return true;
        }

        if (captured.TryGetValue(secondaryKey, out var secondary) && int.TryParse(secondary, out value))
        {
            if (secondaryKey == "SYY" && value is >= 0 and <= 99)
            {
                value += 2000;
            }
            return true;
        }

        return false;
    }

    private static bool TryParseBroadcastAtFromFileName(string fileNameWithoutExtension, out DateTimeOffset broadcastAt, out string title)
    {
        broadcastAt = default;
        title = string.Empty;

        var match = Regex.Match(fileNameWithoutExtension, @"^(?<dt>\d{14})_(?<title>.+)$", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        var dtText = match.Groups["dt"].Value;
        title = match.Groups["title"].Value;
        if (!DateTime.TryParseExact(
                dtText,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dateTime))
        {
            return false;
        }

        broadcastAt = new DateTimeOffset(dateTime, TimeSpan.FromHours(9));
        return true;
    }

    private static string NormalizePathForTemplate(string path)
    {
        return path.Replace('\\', '/');
    }
}
