using System.ComponentModel.DataAnnotations;
using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.RdbContext
{
    /// <summary>
    /// 一回のみまたは繰り返し予約の場合の録音予約情報
    /// </summary>
    public class ProgramReserve
    {
        /// <summary>
        /// 予約ID
        /// </summary>
        [Key]
        [MaxLength(26)]
        public Ulid Id { get; set; }

        /// <summary>
        /// 予約タイプ
        /// </summary>
        public ReservationType ReservationType { get; set; }

        /// <summary>
        /// サービス種別
        /// </summary>
        public RadioServiceKind RadioServiceKind { get; set; }

        /// <summary>
        /// 予約名称
        /// </summary>
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 放送局ID
        /// </summary>
        [MaxLength(20)]
        public string RadioStationId { get; set; } = string.Empty;

        /// <summary>
        /// ファイル名
        /// </summary>
        [MaxLength(200)]
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// フォルダパス
        /// </summary>
        [MaxLength(500)]
        public string FolderPath { get; set; } = string.Empty;

        /// <summary>
        /// 開始時間
        /// </summary>
        public TimeOnly StartTime { get; set; }

        /// <summary>
        /// 終了時間
        /// </summary>
        public TimeOnly EndTime { get; set; }

        /// <summary>
        /// 有効無効
        /// </summary>
        public bool IsEnable { get; set; }

        /// <summary>
        /// タイムフリー録音か否か
        /// </summary>
        public bool IsTimeFree { get; set; }

        /// <summary>
        /// 開始時間のディレイ
        /// </summary>
        public TimeSpan StartDelay { get; set; }

        /// <summary>
        /// 終了時間のディレイ
        /// </summary>
        public TimeSpan EndDelay { get; set; }

        /// <summary>
        /// 予約対象の番組表ID(一回のみ録音用)
        /// </summary>
        [MaxLength(100)]
        public string ProgramId { get; set; } = string.Empty;

        /// <summary>
        /// タグID
        /// </summary>
        public Guid? TagId { get; set; }
    }
}
