namespace RadiCorder.Logics.Services;

/// <summary>
/// radiko proxy 用の短命キーを発行・解決する
/// </summary>
public interface IRadikoProxyTicketService
{
    /// <summary>
    /// 認証トークンに対応する短命キーを発行する
    /// </summary>
    string IssueTokenTicket(string token);

    /// <summary>
    /// 短命キーから認証トークンを解決する
    /// </summary>
    bool TryGetToken(string ticket, out string token);
}
