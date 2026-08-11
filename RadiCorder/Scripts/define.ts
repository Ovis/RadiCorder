export enum ReserveType {
    Undefined = 0,
    Program = 1,
    Keyword = 2
}

export enum RecordingType {
    Undefined = 0,
    RealTime = 1,
    TimeFree = 2,
    Immediate = 3,
    OnDemand = 4
}

export enum RadioServiceKind {
    Undefined = 0,
    Radiko = 1,
    Radiru = 2,
    Other = 99
}
export enum AvailabilityTimeFree {

    /// <summary>
    /// 利用可能
    /// </summary>
    Available = 0,

    /// <summary>
    /// 一部利用可能
    /// </summary>
    PartiallyAvailable = 1,

    /// <summary>
    /// 利用不可
    /// </summary>
    Unavailable = 2,
}



export const ReserveTypeMap: { [key in ReserveType]: { displayName: string } } = {
    [ReserveType.Undefined]: { displayName: "未定義" },
    [ReserveType.Program]: { displayName: "番組表予約" },
    [ReserveType.Keyword]: { displayName: "自動予約ルール" }
};

export const RecordingTypeMap: { [key in RecordingType]: { displayName: string } } = {
    [RecordingType.Undefined]: { displayName: "未定義" },
    [RecordingType.RealTime]: { displayName: "通常録音" },
    [RecordingType.TimeFree]: { displayName: "タイムフリー" },
    [RecordingType.Immediate]: { displayName: "即時実行" },
    [RecordingType.OnDemand]: { displayName: "聞き逃し配信" }
};

export const RadioServiceKindMap: { [key in RadioServiceKind]: { displayName: string, codeId?: string } } = {
    [RadioServiceKind.Undefined]: { displayName: "未定義" },
    [RadioServiceKind.Radiko]: { displayName: "radiko", codeId: "Radiko" },
    [RadioServiceKind.Radiru]: { displayName: "らじる\u2605らじる", codeId: "Radiru" },
    [RadioServiceKind.Other]: { displayName: "その他" }
};
