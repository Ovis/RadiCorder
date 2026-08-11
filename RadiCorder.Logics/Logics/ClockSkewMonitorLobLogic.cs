using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Logics;

/// <summary>
/// NTPサーバーと時刻を比較し、しきい値以上のずれを通知する。
/// </summary>
public class ClockSkewMonitorLobLogic(
    ILogger<ClockSkewMonitorLobLogic> logger,
    IAppConfigurationService appConfigurationService,
    NotificationLobLogic notificationLobLogic)
{
    public async ValueTask CheckAndNotifyClockSkewAsync(CancellationToken cancellationToken = default)
    {
        var server = string.IsNullOrWhiteSpace(appConfigurationService.ClockSkewNtpServer)
            ? "time.google.com"
            : appConfigurationService.ClockSkewNtpServer.Trim();
        var thresholdSeconds = NormalizePositive(appConfigurationService.ClockSkewThresholdSeconds, 30);

        var (ntpUtc, measuredAtUtc) = await QueryNtpUtcAsync(server, cancellationToken);
        var skew = ntpUtc - measuredAtUtc;
        var absSkewSeconds = Math.Abs(skew.TotalSeconds);
        if (absSkewSeconds < thresholdSeconds)
        {
            return;
        }

        var sign = skew.TotalSeconds >= 0 ? "+" : "-";
        logger.ZLogWarning(
            $"NTP時刻ずれを検知しました。server={server} diff={sign}{absSkewSeconds:F1}秒 threshold={thresholdSeconds}秒 ntpUtc={ntpUtc:O} localUtc={measuredAtUtc:O}");

        var message =
            $"時刻のずれが設定値（{thresholdSeconds}秒）を超えました。時間を合わせてください。";

        await notificationLobLogic.SetNotificationAsync(
            LogLevel.Warning,
            NoticeCategory.ClockSkewDetected,
            message);
    }

    private static int NormalizePositive(int value, int fallback)
    {
        return value > 0 ? value : fallback;
    }

    private async ValueTask<(DateTimeOffset NtpUtc, DateTimeOffset MeasuredAtUtc)> QueryNtpUtcAsync(
        string server,
        CancellationToken cancellationToken)
    {
        using var udp = new UdpClient();
        udp.Connect(server, 123);

        var request = new byte[48];
        request[0] = 0x1B;

        var sendAtUtc = DateTimeOffset.UtcNow;
        await udp.SendAsync(request, request.Length).WaitAsync(cancellationToken);

        var receiveResult = await udp.ReceiveAsync().WaitAsync(cancellationToken);
        var receiveAtUtc = DateTimeOffset.UtcNow;
        if (receiveResult.Buffer.Length < 48)
        {
            throw new InvalidOperationException("NTPレスポンスが不正です。");
        }

        var ntpUtc = ReadNtpTimestamp(receiveResult.Buffer, 40);
        var midpointUtc = sendAtUtc + TimeSpan.FromTicks((receiveAtUtc - sendAtUtc).Ticks / 2);

        logger.ZLogDebug(
            $"NTP応答を受信しました。server={server} ntpUtc={ntpUtc:O} localMidpointUtc={midpointUtc:O}");

        return (ntpUtc, midpointUtc);
    }

    private static DateTimeOffset ReadNtpTimestamp(byte[] packet, int offset)
    {
        const ulong ntpEpochOffsetSeconds = 2_208_988_800UL; // 1900-01-01 to 1970-01-01

        var secondsPart =
            ((ulong)packet[offset] << 24) |
            ((ulong)packet[offset + 1] << 16) |
            ((ulong)packet[offset + 2] << 8) |
            packet[offset + 3];
        var fractionPart =
            ((ulong)packet[offset + 4] << 24) |
            ((ulong)packet[offset + 5] << 16) |
            ((ulong)packet[offset + 6] << 8) |
            packet[offset + 7];

        var unixSeconds = (long)(secondsPart - ntpEpochOffsetSeconds);
        var ticks = (long)((fractionPart * TimeSpan.TicksPerSecond) >> 32);
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).AddTicks(ticks);
    }
}
