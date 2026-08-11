using System.ComponentModel.DataAnnotations;

namespace RadiCorder.Logics.RdbContext
{
    /// <summary>
    /// 録音メタデータ
    /// </summary>
    public class RecordingMetadata
    {
        /// <summary>
        /// 録音ID
        /// </summary>
        [Key]
        [MaxLength(26)]
        public Ulid RecordingId { get; set; }

        /// <summary>
        /// 放送局名
        /// </summary>
        [MaxLength(150)]
        public string StationName { get; set; } = string.Empty;

        /// <summary>
        /// 番組タイトル
        /// </summary>
        [MaxLength(100)]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 番組サブタイトル
        /// </summary>
        [MaxLength(100)]
        public string Subtitle { get; set; } = string.Empty;

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
        /// 番組URL
        /// </summary>
        [MaxLength(250)]
        public string ProgramUrl { get; set; } = string.Empty;

        public Recording? Recording { get; set; }
    }
}
