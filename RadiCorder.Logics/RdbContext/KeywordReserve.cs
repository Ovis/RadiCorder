using System.ComponentModel.DataAnnotations;
using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.RdbContext
{
    /// <summary>
    /// キーワード録音予約情報
    /// </summary>
    public class KeywordReserve
    {
        /// <summary>
        /// 予約ID
        /// </summary>
        [Key]
        [MaxLength(26)]
        public Ulid Id { get; set; }

        /// <summary>
        /// キーワード
        /// </summary>
        [MaxLength(255)]
        public string Keyword { get; set; } = string.Empty;

        /// <summary>
        /// 除外キーワード
        /// </summary>
        [MaxLength(255)]
        public string ExcludedKeyword { get; set; } = string.Empty;

        /// <summary>
        /// タイトルのみ検索対象とするか否か
        /// </summary>
        public bool IsTitleOnly { get; set; }
        /// <summary>
        /// タイトルのみ検索対象とするか否か
        /// </summary>
        public bool IsExcludeTitleOnly { get; set; }

        /// <summary>
        /// ファイル名
        /// </summary>
        [MaxLength(200)]
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// フォルダパス
        /// </summary>
        [MaxLength(500)]
        public string FolderPath { get; set; } = string.Empty;

        /// <summary>
        /// 絞り込み用開始時間
        /// </summary>
        public TimeOnly StartTime { get; set; }

        /// <summary>
        /// 絞り込み用終了時間
        /// </summary>
        public TimeOnly EndTime { get; set; }

        /// <summary>
        /// 有効無効
        /// </summary>
        public bool IsEnable { get; set; }

        /// <summary>
        /// 録音対象の曜日
        /// </summary>
        public DaysOfWeek DaysOfWeek { get; set; }

        /// <summary>
        /// 開始時間のディレイ
        /// </summary>
        public TimeSpan? StartDelay { get; set; }

        /// <summary>
        /// 終了時間のディレイ
        /// </summary>
        public TimeSpan? EndDelay { get; set; }

        /// <summary>
        /// ルール適用順
        /// </summary>
        public int SortOrder { get; set; }

        /// <summary>
        /// タグマージ挙動（Default/ForceMerge/ForceSingle）
        /// </summary>
        public KeywordReserveTagMergeBehavior MergeTagBehavior { get; set; } = KeywordReserveTagMergeBehavior.Default;

        /// <summary>
        /// タグ管理
        /// </summary>
        public ICollection<KeywordReserveTagRelation> KeywordReserveTagRelations { get; set; } = new List<KeywordReserveTagRelation>();

        public ICollection<ScheduleJobKeywordReserveRelation> ScheduleJobRelations { get; set; } =
            new List<ScheduleJobKeywordReserveRelation>();
    }
}
