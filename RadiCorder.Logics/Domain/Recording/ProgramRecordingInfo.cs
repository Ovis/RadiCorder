namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 録音対象の番組情報
/// </summary>
/// <param name="ProgramId">番組ID</param>
/// <param name="Title">番組タイトル</param>
/// <param name="Subtitle">番組サブタイトル</param>
/// <param name="StationId">放送局ID</param>
/// <param name="StationName">放送局名</param>
/// <param name="AreaId">エリアID</param>
/// <param name="StartTime">開始日時</param>
/// <param name="EndTime">終了日時</param>
/// <param name="Performer">出演者</param>
/// <param name="Description">番組説明</param>
/// <param name="ProgramUrl">番組URL</param>
/// <param name="ImageUrl">番組イメージURL</param>
public record ProgramRecordingInfo(
    string ProgramId,
    string Title,
    string Subtitle,
    string StationId,
    string StationName,
    string AreaId,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    string Performer,
    string Description,
    string ProgramUrl,
    string ImageUrl = "");
