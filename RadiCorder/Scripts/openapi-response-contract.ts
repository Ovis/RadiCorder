import type { components } from './generated/openapi-types';

/**
 * OpenAPI(v1.json) のレスポンススキーマを基準にしたフロント利用型。
 * 生成型の optional / number|string をUI扱いやすい形に正規化する。
 */
type Schema<Name extends keyof components['schemas']> = components['schemas'][Name];
type RequiredNonNullable<T> = { [K in keyof T]-?: NonNullable<T[K]> };
// OpenAPI生成型の `number | string`（整数/小数の文字列受け取り許容）かどうかを判定する。
type IsNumberStringUnion<T> =
    [T] extends [number | string]
    ? ([number] extends [T] ? ([string] extends [T] ? true : false) : false)
    : false;
type NormalizeValue<T> =
    IsNumberStringUnion<T> extends true ? string :
    [T] extends [(infer U)[]] ? NormalizeValue<U>[] :
    [T] extends [object] ? Normalize<T> :
    T;
type Normalize<T> = { [K in keyof RequiredNonNullable<T>]: NormalizeValue<RequiredNonNullable<T>[K]> };

type WithStringId<T> = Omit<T, 'id'> & { id: string };

export type TagEntryResponseContract = Normalize<Schema<'TagEntry'>>;
export type DateElementResponseContract = Normalize<Schema<'RadioDateEntry'>>;
export type ProgramForApiResponseContract = Normalize<Schema<'ProgramForApiEntry'>>;
export type RadioProgramResponseContract = Normalize<Schema<'RadioProgramEntry'>>;
export type ProgramAreaResponseContract = Omit<Normalize<Schema<'ProgramAreaEntry'>>, 'areaOrder' | 'serviceOrder'> & {
    areaOrder: number;
    serviceOrder: number;
};
export type ScheduleEntryResponseContract = WithStringId<Normalize<Schema<'ScheduleEntry'>>>;
export type RecordedProgramResponseContract = Omit<WithStringId<Normalize<Schema<'RecordedProgramEntry'>>>, 'duration'> & {
    duration: number;
};
export type RecordedStationFilterResponseContract = Normalize<Schema<'RecordedStationFilterEntry'>>;
export type ListRecordingsResponseContract = Omit<Normalize<Schema<'ListRecordingsResponse'>>, 'totalRecords' | 'page' | 'pageSize' | 'recordings'> & {
    totalRecords: number;
    page: number;
    pageSize: number;
    recordings: RecordedProgramResponseContract[];
};
export type RecordedDuplicateSideResponseContract = Omit<Normalize<Schema<'RecordedDuplicateSideEntry'>>, 'durationSeconds'> & {
    durationSeconds: number;
};
export type RecordedDuplicateCandidateResponseContract = Omit<
    Normalize<Schema<'RecordedDuplicateCandidateEntry'>>,
    'left' | 'right' | 'phase1Score' | 'audioScore' | 'finalScore' | 'startTimeDiffHours' | 'durationDiffSeconds'
> & {
    left: RecordedDuplicateSideResponseContract;
    right: RecordedDuplicateSideResponseContract;
    phase1Score: number;
    audioScore: number;
    finalScore: number;
    startTimeDiffHours: number;
    durationDiffSeconds: number;
};
export type RecordedDuplicateDetectionStatusResponseContract = Normalize<Schema<'RecordedDuplicateDetectionStatusEntry'>>;
export type ExternalImportValidationErrorResponseContract = Normalize<Schema<'ExternalImportValidationError'>>;
export type ExternalImportSaveResultResponseContract = Normalize<Schema<'ExternalImportSaveResult'>>;
export type ExternalImportCandidateResponseContract = Normalize<Schema<'ExternalImportCandidateEntry'>>;
export type KeywordReserveResponseContract = Omit<
    Normalize<Schema<'KeywordReserveEntry'>>,
    'id' | 'sortOrder' | 'selectedDaysOfWeek' | 'startDelay' | 'endDelay' | 'mergeTagBehavior'
> & {
    id: string;
    sortOrder: number;
    selectedDaysOfWeek: number[];
    startDelay: number | null;
    endDelay: number | null;
    mergeTagBehavior: number;
};
export type RecordingFileMaintenanceActionDetailResponseContract = Normalize<Schema<'RecordingFileMaintenanceActionDetail'>>;
export type RecordingFileMaintenanceEntryResponseContract = Omit<Normalize<Schema<'RecordingFileMaintenanceEntry'>>, 'candidateCount'> & {
    candidateCount: number;
    isSelected?: boolean;
};
export type RecordingFileMaintenanceScanResultResponseContract =
    Omit<Normalize<Schema<'RecordingFileMaintenanceScanResult'>>, 'missingCount' | 'entries'> & {
        missingCount: number;
        entries: RecordingFileMaintenanceEntryResponseContract[];
    };
export type RecordingFileMaintenanceActionResultResponseContract =
    Omit<Normalize<Schema<'RecordingFileMaintenanceActionResult'>>, 'targetCount' | 'successCount' | 'skipCount' | 'failCount' | 'details'> & {
        targetCount: number;
        successCount: number;
        skipCount: number;
        failCount: number;
        details: RecordingFileMaintenanceActionDetailResponseContract[];
    };
export type TagBulkOperationResultResponseContract =
    Omit<Normalize<Schema<'TagBulkOperationResult'>>, 'successCount' | 'skipCount' | 'failCount'> & {
        successCount: number;
        skipCount: number;
        failCount: number;
    };
export type RecordingBulkDeleteResultResponseContract =
    Omit<Normalize<Schema<'RecordingBulkDeleteResponse'>>, 'successCount' | 'skipCount' | 'failCount'> & {
        successCount: number;
        skipCount: number;
        failCount: number;
    };
export type RecordingBulkListenedResultResponseContract =
    Omit<Normalize<Schema<'RecordingBulkListenedResponse'>>, 'successCount' | 'skipCount' | 'failCount'> & {
        successCount: number;
        skipCount: number;
        failCount: number;
    };
export type NotificationEntryResponseContract = Pick<Normalize<Schema<'NotificationEntry'>>, 'timestamp' | 'message'>;
export type NotificationLatestResponseContract = Omit<Normalize<Schema<'NotificationLatestResponse'>>, 'count' | 'list'> & {
    count: number;
    list: NotificationEntryResponseContract[];
};
export type NotificationListResponseContract = Omit<Normalize<Schema<'NotificationListResponse'>>, 'totalRecords' | 'recordings'> & {
    totalRecords: number;
    recordings: NotificationEntryResponseContract[];
};
export type ProgramNowOnAirResponseContract = Omit<Normalize<Schema<'ProgramNowOnAirResponse'>>, 'programs'> & {
    areas: ProgramAreaResponseContract[];
    programs: RadioProgramResponseContract[];
};
export type ProgramPlaybackInfoResponseContract = Normalize<Schema<'ProgramPlaybackInfoResponse'>>;

// UI側では areaId/areaName を共通利用するため、radiko/radiruの差分をここで吸収する。
export type StationResponseContract = {
    areaId: string;
    areaName: string;
    stationId: string;
    stationName: string;
};
export type StationDataResponseContract = Record<string, StationResponseContract[]>;

export interface ApiResponseContract<T> {
    success: boolean;
    data: T;
    error?: { code?: string; message: string } | null;
    message?: string | null;
}
