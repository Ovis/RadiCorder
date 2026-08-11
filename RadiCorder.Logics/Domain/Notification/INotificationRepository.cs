using RadiCorder.Logics.Logics.NotificationLogic;

namespace RadiCorder.Logics.Domain.Notification;

/// <summary>
/// お知らせの永続化を担うリポジトリ
/// </summary>
public interface INotificationRepository
{
    /// <summary>
    /// 未読のお知らせ件数を取得する
    /// </summary>
    /// <param name="categories"></param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>未読件数</returns>
    ValueTask<int> GetUnreadCountAsync(
        IEnumerable<NoticeCategory>? categories = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 未読のお知らせ一覧を取得する
    /// </summary>
    /// <param name="categories"></param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>未読のお知らせ一覧</returns>
    ValueTask<List<RdbContext.Notification>> GetUnreadListAsync(
        IEnumerable<NoticeCategory>? categories = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定日時以前のお知らせを既読にする
    /// </summary>
    /// <param name="dateTime">基準日時</param>
    /// <param name="categories"></param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    ValueTask MarkReadBeforeAsync(
        DateTimeOffset dateTime,
        IEnumerable<NoticeCategory>? categories = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// お知らせのページング一覧を取得する
    /// </summary>
    /// <param name="page">ページ番号(1始まり)</param>
    /// <param name="pageSize">ページサイズ</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>総件数と一覧</returns>
    ValueTask<(int Total, List<RdbContext.Notification> List)> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// お知らせを登録する
    /// </summary>
    /// <param name="notification">登録対象</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    ValueTask AddAsync(RdbContext.Notification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// お知らせを全件削除する
    /// </summary>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    ValueTask DeleteAllAsync(CancellationToken cancellationToken = default);
}
