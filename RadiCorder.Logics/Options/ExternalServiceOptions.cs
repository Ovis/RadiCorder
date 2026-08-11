namespace RadiCorder.Logics.Options;

/// <summary>
/// 外部サービス接続時の通信関連設定。
/// </summary>
public class ExternalServiceOptions
{
    /// <summary>
    /// 外部サービス接続時に利用するUser-Agent。
    /// </summary>
    public string ExternalServiceUserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.7632.76 Safari/537.36";

    /// <summary>
    /// らじる★らじる番組表APIへの連続リクエスト時の最小待機時間（ミリ秒）。
    /// </summary>
    public int RadiruApiMinRequestIntervalMs { get; set; } = 150;

    /// <summary>
    /// らじる★らじる番組表APIへのアクセス間隔に加算するランダム揺らぎ（ミリ秒）。
    /// </summary>
    public int RadiruApiRequestJitterMs { get; set; } = 100;
}

