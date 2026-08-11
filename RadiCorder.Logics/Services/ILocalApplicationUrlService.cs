namespace RadiCorder.Logics.Services;

/// <summary>
/// 現在起動中アプリのローカルアクセス用ベースURLを保持する
/// </summary>
public interface ILocalApplicationUrlService
{
    /// <summary>
    /// アプリ自身へアクセス可能なベースURLを返す
    /// </summary>
    string? GetBaseUrl();

    /// <summary>
    /// 起動中アプリのバインドURL候補を更新する
    /// </summary>
    void SetCandidateUrls(IEnumerable<string> urls);
}
