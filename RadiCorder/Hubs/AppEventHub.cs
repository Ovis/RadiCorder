using Microsoft.AspNetCore.SignalR;

namespace RadiCorder.Hubs;

/// <summary>
/// 全画面通知イベント配信用の SignalR Hub。
/// </summary>
public class AppEventHub : Hub
{
}

/// <summary>
/// 全画面通知Hubの送信メソッド名定義。
/// </summary>
public static class AppEventHubMethods
{
    /// <summary>
    /// グローバルトースト通知イベント名。
    /// </summary>
    public const string Toast = "toast";

    /// <summary>
    /// 機能別処理イベント名。
    /// </summary>
    public const string Operation = "operation";
}
