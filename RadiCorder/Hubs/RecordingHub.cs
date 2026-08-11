using Microsoft.AspNetCore.SignalR;

namespace RadiCorder.Hubs;

/// <summary>
/// 録音イベント配信用の SignalR Hub。
/// </summary>
public class RecordingHub : Hub
{
}

/// <summary>
/// 録音Hubの送信メソッド名定義。
/// </summary>
public static class RecordingHubMethods
{
    /// <summary>
    /// 録音状態変更通知イベント名。
    /// </summary>
    public const string RecordingStateChanged = "recordingStateChanged";
}
