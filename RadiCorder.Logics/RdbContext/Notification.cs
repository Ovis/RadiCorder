using System.ComponentModel.DataAnnotations;
using RadiCorder.Logics.Logics.NotificationLogic;

namespace RadiCorder.Logics.RdbContext
{
    public class Notification
    {
        /// <summary>
        /// ID
        /// </summary>
        [Key]
        public Ulid Id { get; set; }

        /// <summary>
        /// ログレベル
        /// </summary>
        [MaxLength(20)]
        public string LogLevel { get; set; } = string.Empty;

        /// <summary>
        /// カテゴリ
        /// </summary>
        public NoticeCategory Category { get; set; }

        /// <summary>
        /// 通知本文
        /// </summary>
        [MaxLength(1000)]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// タイムスタンプ
        /// </summary>
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>
        /// 既読フラグ
        /// </summary>
        public bool IsRead { get; set; }
    }
}
