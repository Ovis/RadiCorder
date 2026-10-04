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
    public static void AppendHeaders(FfmpegCommandBuilder command, IReadOnlyDictionary<string, string> headers)
    {
        if (headers.Count == 0)
            return;

        if (headers.Any(h => string.IsNullOrWhiteSpace(h.Key) || h.Key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') ||
            h.Value.Contains('\r') || h.Value.Contains('\n')))
            throw new ArgumentException("録音元のHTTPヘッダーが不正です。", nameof(headers));
        var headerValue = string.Join("\r\n", headers.Select(h => $"{h.Key}: {h.Value}")) + "\r\n";
        command.Add("-headers", headerValue);
    }

    public static void AppendUserAgent(FfmpegCommandBuilder command, string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return;
        }

        command.Add("-user_agent", userAgent.ToSafeNameAndSafeCommandParameter());
    }

    public static void AppendProgramInfo(FfmpegCommandBuilder command, ProgramRecordingInfo programInfo)
    {
        command.Add("-metadata", "title=" + programInfo.Title.ToSafeNameAndSafeCommandParameter());
        command.Add("-metadata", "comment=" + programInfo.Description.ExtractTextFromHtml().ToSafeNameAndSafeCommandParameter());
        command.Add("-metadata", "artist=" + programInfo.Performer.ToSafeNameAndSafeCommandParameter());
        command.Add("-metadata", $"date={programInfo.StartTime.ToJapanDateTime()}");
    }

    public static void AppendAudio(FfmpegCommandBuilder command, RecordingAcquisitionPlan plan)
    {
        command.Append(plan.AudioOutput == RecordingAudioOutput.CopyAac
            ? " -acodec copy -vn -bsf:a aac_adtstoasc" : " -acodec aac -vn");
    }
}
