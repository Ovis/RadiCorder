using System.ComponentModel.DataAnnotations;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.RdbContext
{
    /// <summary>
    /// 録音処理の状態管理
    /// </summary>
    public class Recording
    {
        /// <summary>
        /// 録音ID
        /// </summary>
        [Key]
        [MaxLength(26)]
        public Ulid Id { get; set; }

        /// <summary>
        /// 配信サービス種別
        /// </summary>
        public RadioServiceKind ServiceKind { get; set; }

        /// <summary>
        /// 番組ID
        /// </summary>
        [MaxLength(100)]
        public string ProgramId { get; set; } = string.Empty;

        /// <summary>
        /// 放送局ID
        /// </summary>
        [MaxLength(20)]
        public string StationId { get; set; } = string.Empty;

        /// <summary>
        /// エリアID
        /// </summary>
        [MaxLength(10)]
        public string AreaId { get; set; } = string.Empty;

        /// <summary>
        /// 録音開始日時
        /// </summary>
        public DateTimeOffset StartDateTime { get; set; }

        /// <summary>
        /// 録音終了日時
        /// </summary>
        public DateTimeOffset EndDateTime { get; set; }

        /// <summary>
        /// タイムフリー録音かどうか
        /// </summary>
        public bool IsTimeFree { get; set; }

        /// <summary>
        /// 録音状態
        /// </summary>
        public RecordingState State { get; set; }

        /// <summary>
        /// 失敗時のメッセージ
        /// </summary>
        [MaxLength(1000)]
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 作成日時
        /// </summary>
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>
        /// 更新日時
        /// </summary>
        public DateTimeOffset UpdatedAt { get; set; }

        /// <summary>
        /// 取込元種別
        /// </summary>
        public RecordingSourceType SourceType { get; set; } = RecordingSourceType.Recorded;

        /// <summary>
        /// 視聴済みかどうか
        /// </summary>
        public bool IsListened { get; set; }

        public RecordingFile? RecordingFile { get; set; }
        public RecordingMetadata? RecordingMetadata { get; set; }
    }
}
