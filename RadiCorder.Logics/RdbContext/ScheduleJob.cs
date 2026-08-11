using System.ComponentModel.DataAnnotations;
using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.RdbContext
{
    public class ScheduleJob
    {
        [Key]
        [MaxLength(26)]
        public Ulid Id { get; set; }

        /// <summary>
        /// キーワード予約ID
        /// </summary>
        public Ulid? KeywordReserveId { get; set; }

        /// <summary>
        /// 配信サービス
        /// </summary>
        public RadioServiceKind ServiceKind { get; set; }

        /// <summary>
        /// 放送局ID
        /// </summary>
        [MaxLength(20)]
        public string StationId { get; set; } = string.Empty;

        /// <summary>
        /// エリアID
        /// </summary>
        [MaxLength(5)]
        public string AreaId { get; set; } = string.Empty;

        /// <summary>
        /// プログラムID
        /// </summary>
        [MaxLength(100)]
        public string ProgramId { get; set; } = string.Empty;

        /// <summary>
        /// 番組タイトル
        /// </summary>
        [MaxLength(100)]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// サブタイトル
        /// </summary>
        [MaxLength(100)]
        public string Subtitle { get; set; } = string.Empty;

        /// <summary>
        /// ファイルパス
        /// </summary>
        [MaxLength(500)]
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// 放送開始日時
        /// </summary>
        public DateTimeOffset StartDateTime { get; set; }

        /// <summary>
        /// 放送終了日時
        /// </summary>
        public DateTimeOffset EndDateTime { get; set; }

        /// <summary>
        /// 開始時間のディレイ
        /// </summary>
        public TimeSpan? StartDelay { get; set; }

        /// <summary>
        /// 終了時間のディレイ
        /// </summary>
        public TimeSpan? EndDelay { get; set; }

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
        /// 実行状態
        /// </summary>
        public ScheduleJobState State { get; set; } = ScheduleJobState.Pending;

        /// <summary>
        /// 準備開始UTC
        /// </summary>
        public DateTimeOffset PrepareStartUtc { get; set; }

        /// <summary>
        /// 実行キュー投入UTC
        /// </summary>
        public DateTimeOffset? QueuedAtUtc { get; set; }

        /// <summary>
        /// 録音開始UTC
        /// </summary>
        public DateTimeOffset? ActualStartUtc { get; set; }

        /// <summary>
        /// 完了UTC
        /// </summary>
        public DateTimeOffset? CompletedUtc { get; set; }

        /// <summary>
        /// リトライ回数
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>
        /// 失敗分類
        /// </summary>
        public ScheduleJobErrorCode LastErrorCode { get; set; } = ScheduleJobErrorCode.None;

        /// <summary>
        /// 失敗詳細
        /// </summary>
        [MaxLength(500)]
        public string? LastErrorDetail { get; set; }

        /// <summary>
        /// タグID
        /// </summary>
        public Guid? TagId { get; set; }

        public ICollection<ScheduleJobKeywordReserveRelation> KeywordReserveRelations { get; set; } =
            new List<ScheduleJobKeywordReserveRelation>();
    }
}
