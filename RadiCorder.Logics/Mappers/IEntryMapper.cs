using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.NhkRadiru;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Mappers;

/// <summary>
/// DTO/Entity/Domain変換を集約するマッパー
/// </summary>
public interface IEntryMapper
{
    /// <summary>
    /// スケジュールジョブをスケジュールエントリに変換する
    /// </summary>
    ScheduleEntry ToScheduleEntry(ScheduleJob source);

    /// <summary>
    /// radikoの放送局情報をエントリに変換する
    /// </summary>
    RadikoStationInformationEntry ToRadikoStationInformationEntry(RadikoStation source);

    /// <summary>
    /// らじる★らじるの放送局情報をエントリに変換する
    /// </summary>
    RadiruStationEntry ToRadiruStationEntry(RadiruAreaKind areaKind, RadiruStationKind stationKind);

    /// <summary>
    /// radikoの番組情報をエントリに変換する
    /// </summary>
    RadioProgramEntry ToRadioProgramEntry(RadikoProgram source);

    /// <summary>
    /// らじる★らじるの番組情報をエントリに変換する
    /// </summary>
    RadioProgramEntry ToRadioProgramEntry(NhkRadiruProgram source);

    /// <summary>
    /// radikoの番組情報をAPI用エントリに変換する
    /// </summary>
    ProgramForApiEntry ToRadikoProgramForApiEntry(RadikoProgram source);

    /// <summary>
    /// らじる★らじるの番組情報をAPI用エントリに変換する
    /// </summary>
    ProgramForApiEntry ToRadiruProgramForApiEntry(NhkRadiruProgram source);

    /// <summary>
    /// キーワード予約の永続化モデルをエントリに変換する
    /// </summary>
    KeywordReserveEntry ToKeywordReserveEntry(KeywordReserve source, List<Guid>? tagIds = null, List<string>? tagNames = null);

    /// <summary>
    /// 通知の永続化モデルをエントリに変換する
    /// </summary>
    NotificationEntry ToNotificationEntry(Notification source);

    /// <summary>
    /// 通知エントリを永続化モデルに変換する
    /// </summary>
    Notification ToNotification(NotificationEntry source);

    /// <summary>
    /// 録音済み番組の一覧エントリに変換する
    /// </summary>
    RecordedProgramEntry ToRecordedProgramEntry(Recording recording, RecordingMetadata metadata, RecordingFile file, List<string>? tags = null);
}
