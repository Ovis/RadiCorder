namespace RadiCorder.Logics.Models
{
    public class RecordedProgramEntry
    {
        /// <summary>
        /// ID
        /// </summary>
        public Ulid Id { get; set; }

        /// <summary>
        /// 番組タイトル
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 放送局名
        /// </summary>
        public string StationName { get; set; } = string.Empty;

        /// <summary>
        /// 放送開始日時
        /// </summary>
        public DateTimeOffset StartDateTime { get; set; }

        /// <summary>
        /// 放送終了日時
        /// </summary>
        public DateTimeOffset EndDateTime { get; set; }

        /// <summary>
        /// 放送時間
        /// </summary>
        public double Duration { get; set; }

        /// <summary>
        /// ファイルパス
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// タグ一覧
        /// </summary>
        public List<string> Tags { get; set; } = [];

        /// <summary>
        /// 視聴済みかどうか
        /// </summary>
        public bool IsListened { get; set; }
    }
}
