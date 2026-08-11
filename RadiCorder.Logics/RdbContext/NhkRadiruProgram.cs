using System.ComponentModel.DataAnnotations;
using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.RdbContext
{
    public class NhkRadiruProgram
    {
        /// <summary>
        /// 番組ID
        /// </summary>
        [Key]
        [MaxLength(20)]
        public string ProgramId { get; set; } = string.Empty;

        /// <summary>
        /// 放送波ID
        /// </summary>
        [MaxLength(20)]
        public string StationId { get; set; } = string.Empty;

        /// <summary>
        /// エリアID
        /// </summary>
        [MaxLength(5)]
        public string AreaId { get; set; } = string.Empty;



        /// <summary>
        /// 番組名
        /// </summary>
        [MaxLength(100)]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 番組サブタイトル
        /// </summary>
        [MaxLength(100)]
        public string Subtitle { get; set; } = string.Empty;

        /// <summary>
        /// 放送日
        /// </summary>
        public DateOnly RadioDate { get; set; }

        /// <summary>
        /// 曜日
        /// </summary>
        public DaysOfWeek DaysOfWeek { get; set; }

        /// <summary>
        /// 開始日時
        /// </summary>
        public DateTimeOffset StartTime { get; set; }

        /// <summary>
        /// 終了日時
        /// </summary>
        public DateTimeOffset EndTime { get; set; }

        /// <summary>
        /// 出演者
        /// </summary>
        [MaxLength(150)]
        public string Performer { get; set; } = string.Empty;

        /// <summary>
        /// 番組内容
        /// </summary>
        [MaxLength(250)]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// サイトID
        /// </summary>
        [MaxLength(10)]
        public string SiteId { get; set; } = string.Empty;

        /// <summary>
        /// イベントID
        /// </summary>
        [MaxLength(10)]
        public string EventId { get; set; } = string.Empty;

        /// <summary>
        /// 番組URL
        /// </summary>
        [MaxLength(250)]
        public string ProgramUrl { get; set; } = string.Empty;

        /// <summary>
        /// 番組イメージURL
        /// </summary>
        [MaxLength(250)]
        public string ImageUrl { get; set; } = string.Empty;

        /// <summary>
        /// 聞き逃し配信のコンテンツURL
        /// </summary>
        [MaxLength(500)]
        public string? OnDemandContentUrl { get; set; }

        /// <summary>
        /// 聞き逃し配信の有効期限（UTC）
        /// </summary>
        public DateTime? OnDemandExpiresAtUtc { get; set; }
    }
}
