using System.ComponentModel.DataAnnotations;

namespace RadiCorder.Logics.RdbContext
{
    public class RadikoStation
    {
        /// <summary>
        /// ID
        /// </summary>
        [Key]
        [MaxLength(20)]
        public string StationId { get; set; } = string.Empty;

        /// <summary>
        /// 地域コード
        /// </summary>
        [MaxLength(30)]
        public string RegionId { get; set; } = string.Empty;

        /// <summary>
        /// 地域名
        /// </summary>
        [MaxLength(30)]
        public string RegionName { get; set; } = string.Empty;

        /// <summary>
        /// 地域表示順
        /// </summary>
        public int RegionOrder { get; set; }

        /// <summary>
        /// エリアID
        /// </summary>
        [MaxLength(5)]
        public string Area { get; set; } = string.Empty;

        /// <summary>
        /// 放送局名
        /// </summary>
        [MaxLength(50)]
        public string StationName { get; set; } = string.Empty;

        /// <summary>
        /// 放送局ウェブサイトURL
        /// </summary>
        [MaxLength(100)]
        public string StationUrl { get; set; } = string.Empty;

        /// <summary>
        /// 放送局ロゴPath
        /// </summary>
        [MaxLength(250)]
        public string LogoPath { get; set; } = string.Empty;

        /// <summary>
        /// エリアフリー利用可能
        /// </summary>
        public bool AreaFree { get; set; }

        /// <summary>
        /// タイムフリー利用可能
        /// </summary>
        public bool TimeFree { get; set; }

        /// <summary>
        /// 放送局表示順
        /// </summary>
        public int StationOrder { get; set; }

        /// <summary>
        /// 有効な放送局か
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// full.xmlで最後に確認できた日時(UTC)
        /// </summary>
        public DateTimeOffset? LastSeenAtUtc { get; set; }
    }
}
