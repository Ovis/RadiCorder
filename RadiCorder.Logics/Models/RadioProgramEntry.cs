using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Models.Radiko;

namespace RadiCorder.Logics.Models
{
    public class RadioProgramEntry
    {
        /// <summary>
        /// 番組ID
        /// </summary>
        public string ProgramId { get; set; } = string.Empty;

        /// <summary>
        /// 配信サービス種別
        /// </summary>
        public RadioServiceKind ServiceKind { get; set; }

        /// <summary>
        /// 放送エリアID
        /// </summary>
        public string AreaId { get; set; } = string.Empty;

        /// <summary>
        /// 放送エリア名
        /// </summary>
        public string AreaName { get; set; } = string.Empty;

        /// <summary>
        /// 放送局ID
        /// </summary>
        public string StationId { get; set; } = string.Empty;

        /// <summary>
        /// 放送局名
        /// </summary>
        public string StationName { get; set; } = string.Empty;

        /// <summary>
        /// 番組名
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 番組サブタイトル
        /// </summary>
        public string Subtitle { get; set; } = string.Empty;

        /// <summary>
        /// 出演者
        /// </summary>
        public string Performer { get; set; } = string.Empty;

        /// <summary>
        /// 詳細
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 開始日時
        /// </summary>
        public DateTimeOffset StartTime { get; set; }

        /// <summary>
        /// 終了日時
        /// </summary>
        public DateTimeOffset EndTime { get; set; }

        /// <summary>
        /// タイムフリー利用可能
        /// </summary>
        public AvailabilityTimeFree AvailabilityTimeFree { get; set; }

        /// <summary>
        /// 番組URL
        /// </summary>
        public string ProgramUrl { get; set; } = string.Empty;

        /// <summary>
        /// 番組イメージURL
        /// </summary>
        public string ImageUrl { get; set; } = string.Empty;

        /// <summary>
        /// 聞き逃し配信コンテンツURL
        /// </summary>
        public string? OnDemandContentUrl { get; set; }

        /// <summary>
        /// 聞き逃し配信の有効期限（UTC）
        /// </summary>
        public DateTime? OnDemandExpiresAtUtc { get; set; }
    }
}
