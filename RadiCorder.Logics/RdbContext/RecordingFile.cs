using System.ComponentModel.DataAnnotations;

namespace RadiCorder.Logics.RdbContext
{
    /// <summary>
    /// 録音ファイル情報
    /// </summary>
    public class RecordingFile
    {
        /// <summary>
        /// 録音ID
        /// </summary>
        [Key]
        [MaxLength(26)]
        public Ulid RecordingId { get; set; }

        /// <summary>
        /// 保存先の相対パス
        /// </summary>
        [MaxLength(500)]
        public string FileRelativePath { get; set; } = string.Empty;

        /// <summary>
        /// HLS生成済みフラグ
        /// </summary>
        public bool HasHlsFile { get; set; }

        /// <summary>
        /// HLS生成先ディレクトリ
        /// </summary>
        [MaxLength(1000)]
        public string? HlsDirectoryPath { get; set; }

        public Recording? Recording { get; set; }
    }
}
