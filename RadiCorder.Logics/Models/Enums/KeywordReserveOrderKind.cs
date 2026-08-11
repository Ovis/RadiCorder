using RadiCorder.Logics.Primitives.DataAnnotations;

namespace RadiCorder.Logics.Models.Enums
{
    public enum KeywordReserveOrderKind
    {
        /// <summary>
        /// 番組開始日時の昇順
        /// </summary>
        [EnumDisplayName("番組開始日時の昇順")]
        [CodeId("ProgramStartDateTimeAsc")]
        ProgramStartDateTimeAsc = 1,

        /// <summary>
        /// 番組開始日時の降順
        /// </summary>
        [EnumDisplayName("番組開始日時の降順")]
        [CodeId("ProgramStartDateTimeDesc")]
        ProgramStartDateTimeDesc = 2,

        /// <summary>
        /// 番組終了日時の昇順
        /// </summary>
        [EnumDisplayName("番組終了日時の昇順")]
        [CodeId("ProgramEndDateTimeAsc")]
        ProgramEndDateTimeAsc = 3,

        /// <summary>
        /// 番組終了日時の降順
        /// </summary>
        [EnumDisplayName("番組終了日時の降順")]
        [CodeId("ProgramEndDateTimeDesc")]
        ProgramEndDateTimeDesc = 4,

        /// <summary>
        /// 番組名の昇順
        /// </summary>
        [EnumDisplayName("番組名の昇順")]
        [CodeId("ProgramNameAsc")]
        ProgramNameAsc = 5,

        /// <summary>
        /// 番組名の降順
        /// </summary>
        [EnumDisplayName("番組名の降順")]
        [CodeId("ProgramNameDesc")]
        ProgramNameDesc = 6
    }
}
