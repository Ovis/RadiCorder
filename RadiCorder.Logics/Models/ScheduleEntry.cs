using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Models
{
    public class ScheduleEntry
    {
        public Ulid Id { get; set; }

        /// <summary>
        /// 配信サービス
        /// </summary>
        public RadioServiceKind ServiceKind { get; set; }

        /// <summary>
        /// 放送局ID
        /// </summary>
        public string StationId { get; set; } = string.Empty;

        /// <summary>
        /// 放送局名
        /// </summary>
        public string StationName { get; set; } = string.Empty;

        /// <summary>
        /// エリアID
        /// </summary>
        public string AreaId { get; set; } = string.Empty;

        /// <summary>
        /// プログラムID
        /// </summary>
        public string ProgramId { get; set; } = string.Empty;

        /// <summary>
        /// 番組タイトル
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// サブタイトル
        /// </summary>
        public string Subtitle { get; set; } = string.Empty;

        /// <summary>
        /// 放送開始日時
        /// </summary>
        public DateTimeOffset StartDateTime { get; set; }

        /// <summary>
        /// 放送終了日時
        /// </summary>
        public DateTimeOffset EndDateTime { get; set; }

        /// <summary>
        /// 出演者
        /// </summary>
        public string Performer { get; set; } = string.Empty;

        /// <summary>
        /// 番組内容
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 録音方法種別
        /// </summary>
        public RecordingType RecordingType { get; set; }

        /// <summary>
        /// 予約種別
        /// </summary>
        public ReserveType ReserveType { get; set; }

        /// <summary>
        /// 有効無効
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// この録音予定の登録元となったキーワード予約名一覧
        /// </summary>
        public List<string> MatchedKeywordReserveKeywords { get; set; } = [];

        /// <summary>
        /// この録音予定に付与される予定のタグ名一覧
        /// </summary>
        public List<string> PlannedTagNames { get; set; } = [];
    }
}
