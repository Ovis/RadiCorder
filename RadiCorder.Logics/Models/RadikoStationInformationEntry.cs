namespace RadiCorder.Logics.Models
{
    public class RadikoStationInformationEntry
    {
        /// <summary>
        /// ID
        /// </summary>
        public string StationId { get; set; } = string.Empty;

        /// <summary>
        /// 地域コード
        /// </summary>
        public string RegionId { get; set; } = string.Empty;

        /// <summary>
        /// 地域名
        /// </summary>
        public string RegionName { get; set; } = string.Empty;

        /// <summary>
        /// 地域表示順
        /// </summary>
        public int RegionOrder { get; set; }

        /// <summary>
        /// エリアID
        /// </summary>
        public string Area { get; set; } = string.Empty;

        /// <summary>
        /// 放送局名
        /// </summary>
        public string StationName { get; set; } = string.Empty;

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
    }
}
