using Microsoft.AspNetCore.SignalR;

namespace RadiCorder.Hubs;

/// <summary>
/// 録音予定更新イベント配信用の SignalR Hub。
/// </summary>
public class ReserveHub : Hub
{
}

/// <summary>
/// 録音予定Hubの送信メソッド名定義。
/// </summary>
public static class ReserveHubMethods
{
    /// <summary>
    /// 録音予定更新通知イベント名。
    /// </summary>
    public const string ReserveScheduleChanged = "reserveScheduleChanged";
}
