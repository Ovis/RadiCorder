using Microsoft.AspNetCore.SignalR;

namespace RadiCorder.Hubs;

/// <summary>
/// 同一番組候補チェック状態配信用の SignalR Hub。
/// </summary>
public class RecordedDuplicateDetectionHub : Hub
{
}

/// <summary>
/// 同一番組候補チェックHubの送信メソッド名定義。
/// </summary>
public static class RecordedDuplicateDetectionHubMethods
{
    /// <summary>
    /// 同一番組候補チェック状態変更イベント名。
    /// </summary>
    public const string StatusChanged = "duplicateDetectionStatusChanged";
}
