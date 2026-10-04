using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Infrastructure.Recording;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.Extensions;
using ZLogger;

namespace RadiCorder.Logics.Infrastructure.Recording;

internal static class RecordingFfmpegArguments
{
    public static void AppendHeaders(StringBuilder command, IReadOnlyDictionary<string, string> headers)
    {
        if (headers.Count == 0)
            return;

        var headerValue = string.Join("\r\n", headers.Select(h => $"{h.Key}: {h.Value}")) + "\r\n";
        command.Append($" -headers \"{headerValue}\"");
    }

    public static void AppendUserAgent(StringBuilder command, string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return;
        }

        command.Append($" -user_agent \"{userAgent.ToSafeNameAndSafeCommandParameter()}\"");
    }

    public static void AppendProgramInfo(StringBuilder command, ProgramRecordingInfo programInfo)
    {
        command.Append($" -metadata title=\"{programInfo.Title.ToSafeNameAndSafeCommandParameter()}\"");
        command.Append($" -metadata comment=\"{programInfo.Description.ExtractTextFromHtml().ToSafeNameAndSafeCommandParameter()}\"");
        command.Append($" -metadata artist=\"{programInfo.Performer.ToSafeNameAndSafeCommandParameter()}\"");
        command.Append($" -metadata date=\"{programInfo.StartTime.ToJapanDateTime()}\"");
    }

    public static void AppendAudio(StringBuilder command, RecordingAcquisitionPlan plan)
    {
        command.Append(plan.AudioOutput == RecordingAudioOutput.CopyAac
            ? " -acodec copy -vn -bsf:a aac_adtstoasc" : " -acodec aac -vn");
    }
}
