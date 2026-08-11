using RadiCorder.Logics.Logics.NotificationLogic;

namespace RadiCorder.Logics.Models
{
    public class NotificationEntry
    {
        /// <summary>
        /// ログレベル
        /// </summary>
        public string LogLevel { get; set; } = string.Empty;

        /// <summary>
        /// カテゴリ
        /// </summary>
        public NoticeCategory Category { get; set; }

        /// <summary>
        /// 通知本文
        /// </summary>
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
