using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Models.NhkRadiru;

namespace RadiCorder.Logics.Domain.Station;

/// <summary>
/// 放送局情報の永続化を担うリポジトリ
/// </summary>
public interface IStationRepository
{
    /// <summary>
    /// radiko放送局が初期化済みか確認する
    /// </summary>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>初期化済みならtrue</returns>
    ValueTask<bool> HasAnyRadikoStationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// radiko放送局を取得する
    /// </summary>
    /// <param name="activeOnly">有効な放送局のみを取得する場合はtrue</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>放送局一覧</returns>
    ValueTask<List<RadikoStation>> GetRadikoStationsAsync(bool activeOnly = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// radiko放送局を追加または更新し、入力に含まれない既存局を無効化する
    /// </summary>
    /// <param name="stations">放送局一覧</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    ValueTask UpsertRadikoStationsAsync(IEnumerable<RadikoStation> stations, CancellationToken cancellationToken = default);

    /// <summary>
    /// らじる★らじる放送局が初期化済みか確認する
    /// </summary>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>初期化済みならtrue</returns>
    ValueTask<bool> HasAnyRadiruStationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定エリアとサービスIDに対応するらじる★らじるHLS URLを取得する。
    /// 現行のエリア/サービス定義から取得する。
    /// </summary>
    /// <param name="areaId">エリアID</param>
    /// <param name="serviceId">サービスID</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>HLS URL。見つからない場合はnull</returns>
    ValueTask<string?> GetRadiruHlsUrlByAreaAndServiceAsync(
        string areaId,
        string serviceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 新しいエリア/サービス定義テーブルから、らじる★らじる局一覧を取得する。
    /// </summary>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>局一覧。定義が未登録の場合は空配列</returns>
    ValueTask<List<RadiruStationEntry>> GetRadiruStationsFromAreaServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// らじる★らじるのエリア定義とサービス定義を追加または更新する。
    /// 対象エリアのサービス定義は新しい入力で置換される。
    /// </summary>
    /// <param name="areas">エリア定義</param>
    /// <param name="services">サービス定義</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    ValueTask UpsertRadiruAreasAndServicesAsync(
        IEnumerable<NhkRadiruArea> areas,
        IEnumerable<NhkRadiruAreaService> services,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// らじる★らじるの有効なエリアID/サービスID組を取得する。
    /// </summary>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>エリアID/サービスIDの組一覧</returns>
    ValueTask<List<(string AreaId, string ServiceId)>> GetActiveRadiruAreaServiceKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定エリアIDの定義を取得する。
    /// </summary>
    /// <param name="areaId">エリアID</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>エリア定義。存在しない場合はnull</returns>
    ValueTask<NhkRadiruArea?> GetRadiruAreaByAreaIdAsync(string areaId, CancellationToken cancellationToken = default);
}
