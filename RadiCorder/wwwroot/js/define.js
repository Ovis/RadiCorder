export var ReserveType;
(function (ReserveType) {
    ReserveType[ReserveType["Undefined"] = 0] = "Undefined";
    ReserveType[ReserveType["Program"] = 1] = "Program";
    ReserveType[ReserveType["Keyword"] = 2] = "Keyword";
})(ReserveType || (ReserveType = {}));
export var RecordingType;
(function (RecordingType) {
    RecordingType[RecordingType["Undefined"] = 0] = "Undefined";
    RecordingType[RecordingType["RealTime"] = 1] = "RealTime";
    RecordingType[RecordingType["TimeFree"] = 2] = "TimeFree";
    RecordingType[RecordingType["Immediate"] = 3] = "Immediate";
    RecordingType[RecordingType["OnDemand"] = 4] = "OnDemand";
})(RecordingType || (RecordingType = {}));
export var RadioServiceKind;
(function (RadioServiceKind) {
    RadioServiceKind[RadioServiceKind["Undefined"] = 0] = "Undefined";
    RadioServiceKind[RadioServiceKind["Radiko"] = 1] = "Radiko";
    RadioServiceKind[RadioServiceKind["Radiru"] = 2] = "Radiru";
    RadioServiceKind[RadioServiceKind["Other"] = 99] = "Other";
})(RadioServiceKind || (RadioServiceKind = {}));
export var AvailabilityTimeFree;
(function (AvailabilityTimeFree) {
    /// <summary>
    /// 利用可能
    /// </summary>
    AvailabilityTimeFree[AvailabilityTimeFree["Available"] = 0] = "Available";
    /// <summary>
    /// 一部利用可能
    /// </summary>
    AvailabilityTimeFree[AvailabilityTimeFree["PartiallyAvailable"] = 1] = "PartiallyAvailable";
    /// <summary>
    /// 利用不可
    /// </summary>
    AvailabilityTimeFree[AvailabilityTimeFree["Unavailable"] = 2] = "Unavailable";
})(AvailabilityTimeFree || (AvailabilityTimeFree = {}));
export const ReserveTypeMap = {
    [ReserveType.Undefined]: { displayName: "未定義" },
    [ReserveType.Program]: { displayName: "番組表予約" },
    [ReserveType.Keyword]: { displayName: "自動予約ルール" }
};
export const RecordingTypeMap = {
    [RecordingType.Undefined]: { displayName: "未定義" },
    [RecordingType.RealTime]: { displayName: "通常録音" },
    [RecordingType.TimeFree]: { displayName: "タイムフリー" },
    [RecordingType.Immediate]: { displayName: "即時実行" },
    [RecordingType.OnDemand]: { displayName: "聞き逃し配信" }
};
export const RadioServiceKindMap = {
    [RadioServiceKind.Undefined]: { displayName: "未定義" },
    [RadioServiceKind.Radiko]: { displayName: "radiko", codeId: "Radiko" },
    [RadioServiceKind.Radiru]: { displayName: "らじる\u2605らじる", codeId: "Radiru" },
    [RadioServiceKind.Other]: { displayName: "その他" }
};
//# sourceMappingURL=define.js.map