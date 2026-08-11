namespace RadiCorder.Logics.Options;

/// <summary>
/// 新しいリリース確認機能の設定。
/// </summary>
public class ReleaseOptions
{
    /// <summary>
    /// 新しいリリースのチェック間隔（日）。0以下の場合は無効。
    /// </summary>
    public int ReleaseCheckIntervalDays { get; set; } = 1;

    /// <summary>
    /// 新しいリリース確認対象のGitHubオーナー名。
    /// </summary>
    public string ReleaseCheckGitHubOwner { get; set; } = "ovis";

    /// <summary>
    /// 新しいリリース確認対象のGitHubリポジトリ名。
    /// </summary>
    public string ReleaseCheckGitHubRepository { get; set; } = "RadiCorder";
}

