namespace RadiCorder.Features.Shared.Models
{
    /// <summary>
    /// 録音番組一括削除のリクエスト
    /// </summary>
    public class RecordingBulkDeleteRequest
    {
        /// <summary>
        /// 削除対象の録音ID
        /// </summary>
        public List<string> RecordingIds { get; set; } = [];

        /// <summary>
        /// 録音ファイルも削除するかどうか
        /// </summary>
        public bool DeleteFiles { get; set; } = true;
    }
}

