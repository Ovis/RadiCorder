using RadiCorder.Logics.Models.Radiko;
using System.Xml.Linq;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Providers.Radiko;

/// <summary>
/// radiko番組XMLの解析。HTTPやDBを起動せずに応答形式を検証できる。
/// </summary>
public static class RadikoProgramParser
{
    public static (RadikoProgram? Program, bool UsedFallback, List<string> FallbackFields, Exception? StrictError) ParseProgram(XElement programElement, string stationId)
    {
        try
        {
            var ft = programElement.Attribute("ft")?.Value ?? string.Empty;
            var to = programElement.Attribute("to")?.Value ?? string.Empty;

            var start = ft.ToJapaneseDateTime();
            var end = to.ToJapaneseDateTime();
            var tsInNg = programElement.Element("ts_in_ng")?.Value.Trim() ?? throw new DomainException("ts_in_ngが取得できませんでした。");

            var program = new RadikoProgram
            {
                ProgramId = $"{stationId}_{ft + to}",
                StartTime = start.ToUniversalTime(),
                EndTime = end.ToUniversalTime(),
                Title = programElement.Element("title")?.Value.Trim().ToSafeName().To半角英数字() ?? string.Empty,
                Performer = programElement.Element("pfm")?.Value.Trim() ?? string.Empty,
                Description = programElement.Element("info")?.Value.Trim() ??
                              programElement.Element("desc")?.Value.Trim() ?? string.Empty,
                StationId = stationId,
                RadioDate = start.ToRadioDate(),
                DaysOfWeek = start.ToRadioDayOfWeek().ToDaysOfWeek(),
                AvailabilityTimeFree = tsInNg.FromString(),
                ProgramUrl = programElement.Element("url")?.Value.Trim() ?? string.Empty,
                ImageUrl = ExtractProgramImageUrl(programElement)
            };

            return (program, false, [], null);
        }
        catch (Exception ex)
        {
            var fallbackFields = new List<string>();

            var ft = programElement.Attribute("ft")?.Value ?? string.Empty;
            var to = programElement.Attribute("to")?.Value ?? string.Empty;

            if (!TryParseJapaneseDateTime(ft, out var start))
            {
                fallbackFields.Add("ft(startDateTime)");
            }

            if (!TryParseJapaneseDateTime(to, out var end))
            {
                fallbackFields.Add("to(endDateTime)");
            }

            if (start == default || end == default)
            {
                return (null, true, fallbackFields, ex);
            }

            var tsInNg = programElement.Element("ts_in_ng")?.Value.Trim() ?? string.Empty;
            if (!TryParseAvailabilityTimeFree(tsInNg, out var availability))
            {
                fallbackFields.Add("ts_in_ng(availabilityTimeFree)");
            }

            if (string.IsNullOrWhiteSpace(programElement.Element("title")?.Value))
            {
                fallbackFields.Add("title");
            }

            var fallbackProgram = new RadikoProgram
            {
                ProgramId = $"{stationId}_{ft + to}",
                StartTime = start.ToUniversalTime(),
                EndTime = end.ToUniversalTime(),
                Title = programElement.Element("title")?.Value.Trim().ToSafeName().To半角英数字() ?? string.Empty,
                Performer = programElement.Element("pfm")?.Value.Trim() ?? string.Empty,
                Description = programElement.Element("info")?.Value.Trim() ??
                              programElement.Element("desc")?.Value.Trim() ?? string.Empty,
                StationId = stationId,
                RadioDate = start.ToRadioDate(),
                DaysOfWeek = start.ToRadioDayOfWeek().ToDaysOfWeek(),
                AvailabilityTimeFree = availability,
                ProgramUrl = programElement.Element("url")?.Value.Trim() ?? string.Empty,
                ImageUrl = ExtractProgramImageUrl(programElement)
            };

            return (fallbackProgram, true, fallbackFields, ex);
        }
    }

    private static bool TryParseJapaneseDateTime(string value, out DateTimeOffset dateTimeOffset)
    {
        try
        {
            dateTimeOffset = value.ToJapaneseDateTime();
            return true;
        }
        catch
        {
            dateTimeOffset = default;
            return false;
        }
    }

    private static bool TryParseAvailabilityTimeFree(string value, out AvailabilityTimeFree availabilityTimeFree)
    {
        try
        {
            availabilityTimeFree = value.FromString();
            return true;
        }
        catch
        {
            availabilityTimeFree = AvailabilityTimeFree.Unavailable;
            return false;
        }
    }

    public static string BuildProgramContext(XElement programElement)
    {
        var ft = programElement.Attribute("ft")?.Value ?? string.Empty;
        var to = programElement.Attribute("to")?.Value ?? string.Empty;
        var title = programElement.Element("title")?.Value.Trim() ?? string.Empty;
        return $"ft={ft},to={to},title={title}";
    }

    private static string ExtractProgramImageUrl(XElement programElement)
    {
        var imageUrl = programElement.Element("img")?.Value?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(imageUrl) ? string.Empty : imageUrl;
    }
}
