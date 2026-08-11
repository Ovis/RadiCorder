using RadiCorder.Logics.Models;

namespace RadiCorder.Logics.Logics.RecordedRadioLogic
{
    /// <summary>
    /// 録音済み番組の参照/削除/HLS生成を統括するロジック
    /// </summary>
    public class RecordedRadioLobLogic(
        RecordedProgramQueryService queryService,
        RecordedProgramMediaService mediaService,
        RecordedProgramDuplicateDetectionService? duplicateDetectionService = null)
    {
        /// <summary>
        /// 録音済み番組のリストを取得
        /// </summary>
        /// <param name="searchQuery">検索クエリ</param>
        /// <param name="page">ページ番号</param>
        /// <param name="pageSize">ページサイズ</param>
        /// <param name="sortBy">ソートキー</param>
        /// <param name="isDescending">降順かどうか</param>
        /// <param name="withinDays"></param>
        /// <param name="stationId"></param>
        /// <param name="tagIds"></param>
        /// <param name="tagMode"></param>
        /// <param name="untaggedOnly"></param>
        /// <param name="unlistenedOnly"></param>
        public async ValueTask<(bool IsSuccess, int Total, List<RecordedProgramEntry>? List, Exception? Error)> GetRecorderProgramListAsync(
            string searchQuery,
            int page,
            int pageSize,
            string sortBy,
            bool isDescending,
            int? withinDays,
            string stationId,
            List<Guid>? tagIds = null,
            string tagMode = "or",
            bool untaggedOnly = false,
            bool unlistenedOnly = false)
        {
            return await queryService.GetRecorderProgramListAsync(searchQuery, page, pageSize, sortBy, isDescending, withinDays, stationId, tagIds, tagMode, untaggedOnly, unlistenedOnly);
        }

        /// <summary>
        /// 録音済み番組一覧の放送局フィルタ候補を取得
        /// </summary>
        public async ValueTask<(bool IsSuccess, List<RecordedStationFilterEntry>? List, Exception? Error)> GetRecordedStationFiltersAsync()
        {
            return await queryService.GetRecordedStationFiltersAsync();
        }


        /// <summary>
        /// 指定された録音済み番組が存在するかどうかを確認
        /// </summary>
        public async ValueTask<(bool IsSuccess, bool IsExists)> CheckProgramExistsAsync(Ulid recorderId)
        {
            return await queryService.CheckProgramExistsAsync(recorderId);
        }


        /// <summary>
        /// 録音済み番組を削除する
        /// </summary>
        /// <param name="recorderId"></param>
        /// <param name="deletePhysicalFiles">ファイルも削除するかどうか</param>
        public async ValueTask<bool> DeleteRecordedProgramAsync(Ulid recorderId, bool deletePhysicalFiles = true)
        {
            return await mediaService.DeleteRecordedProgramAsync(recorderId, deletePhysicalFiles);
        }


        /// <summary>
        /// 指定された録音済み番組のファイルパスを取得
        /// </summary>
        public async ValueTask<(bool IsSuccess, string FilePath)> GetRecordedProgramFilePathAsync(Ulid recorderId)
        {
            return await mediaService.GetRecordedProgramFilePathAsync(recorderId);
        }


        /// <summary>
        /// HLSファイルのパスを取得
        /// HLSファイルが存在しない場合は生成
        /// </summary>
        public async ValueTask<(bool IsSuccess, string Path)> GetHlsAsync(Ulid recorderId, bool createHls = true)
        {
            return await mediaService.GetHlsAsync(recorderId, createHls);
        }

        /// <summary>
        /// 指定した録音済み番組を視聴済みに更新
        /// </summary>
        public async ValueTask MarkAsListenedAsync(Ulid recorderId)
        {
            await queryService.MarkAsListenedAsync(recorderId);
        }

        /// <summary>
        /// 指定した録音済み番組を一括で既読/未読に更新
        /// </summary>
        public async ValueTask<(int SuccessCount, int SkipCount, int FailCount, List<string> FailedRecordingIds, List<string> SkippedRecordingIds)> BulkUpdateListenedStateAsync(
            IReadOnlyCollection<Ulid> recordingIds,
            bool isListened)
        {
            return await queryService.BulkUpdateListenedStateAsync(recordingIds, isListened);
        }

        /// <summary>
        /// 類似録音候補を抽出する
        /// </summary>
        public async ValueTask<(bool IsSuccess, List<RecordedDuplicateCandidateEntry> List, string? ErrorMessage, Exception? Error)> DetectDuplicatesAsync(
            int lookbackDays,
            int maxPhase1Groups,
            string phase2Mode,
            int broadcastClusterWindowHours,
            double finalThreshold,
            CancellationToken cancellationToken = default)
        {
            if (duplicateDetectionService == null)
            {
                return (false, [], "類似抽出サービスが初期化されていません。", null);
            }

            return await duplicateDetectionService.DetectAsync(lookbackDays, maxPhase1Groups, phase2Mode, broadcastClusterWindowHours, finalThreshold, cancellationToken);
        }
    }
}
