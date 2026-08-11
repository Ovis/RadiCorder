namespace RadiCorder.Logics.Models.Enums;

/// <summary>
/// キーワード予約ごとのタグマージ挙動
/// </summary>
public enum KeywordReserveTagMergeBehavior
{
    /// <summary>
    /// 全体設定に従う
    /// </summary>
    Default = 0,

    /// <summary>
    /// このルールでは常にマージする
    /// </summary>
    ForceMerge = 1,

    /// <summary>
    /// このルールでは常にマージしない
    /// </summary>
    ForceSingle = 2
}

