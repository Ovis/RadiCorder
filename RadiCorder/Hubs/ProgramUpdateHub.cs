using Microsoft.AspNetCore.SignalR;

namespace RadiCorder.Hubs;

/// <summary>
/// 番組表更新状態配信用の SignalR Hub。
/// </summary>
public class ProgramUpdateHub : Hub
{
}

/// <summary>
/// 番組表更新Hubの送信メソッド名定義。
/// </summary>
public static class ProgramUpdateHubMethods
{
    /// <summary>
    /// 番組表更新状態変更イベント名。
    /// </summary>
    public const string ProgramUpdateStatusChanged = "programUpdateStatusChanged";
}
