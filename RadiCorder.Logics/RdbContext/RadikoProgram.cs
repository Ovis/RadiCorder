using System.ComponentModel.DataAnnotations;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Models.Radiko;

namespace RadiCorder.Logics.RdbContext
{
    public class RadikoProgram
    {
        /// <summary>
        /// 番組ID
        /// </summary>
        [Key]
        [MaxLength(50)]
        public string ProgramId { get; set; } = string.Empty;

        /// <summary>
        /// 放送局ID
        /// </summary>
        [MaxLength(10)]
        public string StationId { get; set; } = string.Empty;

        /// <summary>
        /// 番組名
        /// </summary>
        [MaxLength(100)]
        public string Title { get; set; } = string.Empty;

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
        [MaxLength(100)]
        public string Performer { get; set; } = string.Empty;

        /// <summary>
        /// 詳細
        /// </summary>
        [MaxLength(10000)]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// タイムフリー利用可能
        /// </summary>
        public AvailabilityTimeFree AvailabilityTimeFree { get; set; }

        /// <summary>
        /// 番組URL
        /// </summary>
        [MaxLength(300)]
        public string ProgramUrl { get; set; } = string.Empty;

        /// <summary>
        /// 番組イメージURL
        /// </summary>
        [MaxLength(500)]
        public string ImageUrl { get; set; } = string.Empty;
    }
}
