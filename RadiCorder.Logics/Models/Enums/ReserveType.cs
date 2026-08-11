using RadiCorder.Logics.Primitives.DataAnnotations;

namespace RadiCorder.Logics.Models.Enums
{
    /// <summary>
    /// 予約種別
    /// </summary>
    public enum ReserveType
    {
        [EnumDisplayName("未定義")]
        Undefined = 0,

        [EnumDisplayName("番組単位予約")]
        Program = 1,

        [EnumDisplayName("キーワード予約")]
        Keyword = 2,
    }
}
