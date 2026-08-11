using RadiCorder.Logics.Models.NhkRadiru;
using RadiCorder.Logics.Models.NhkRadiru.JsonEntity;

namespace RadiCorder.Logics.Interfaces;

public interface IRadiruApiClient
{
    /// <summary>
    /// 取得対象のエリアID/サービスID組一覧を取得する
    /// </summary>
    /// <param name="targetDateJst">取得対象日(JST)</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>エリアID/サービスID組一覧</returns>
    ValueTask<List<(string AreaId, string ServiceId)>> GetAvailableAreaServicesAsync(
        DateTimeOffset targetDateJst,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定日の番組表を取得する
    /// </summary>
    /// <param name="areaId">エリアID</param>
    /// <param name="serviceId">サービスID</param>
    /// <param name="date">取得する日付</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>番組リスト</returns>
    Task<List<RadiruProgramJsonEntity>> GetDailyProgramsAsync(
        string areaId,
        string serviceId,
        DateTimeOffset date,
        CancellationToken cancellationToken = default);
}
