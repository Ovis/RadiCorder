import type { components } from './generated/openapi-types';

/**
 * OpenAPI生成スキーマから利用するリクエスト型の再エクスポート。
 * スクリプト側で直接 schema 文字列を参照しないための薄い中継層。
 */
export type ProgramInformationRequestContract = components['schemas']['ProgramInformationRequestEntry'];
export type DuplicateDetectionRunRequestContract = components['schemas']['DuplicateDetectionRunRequest'];
export type ReserveEntryRequestContract = components['schemas']['ReserveEntryRequest'];
export type KeywordReserveEntryContract = components['schemas']['KeywordReserveEntry'];
export type ProgramSearchEntityContract = components['schemas']['ProgramSearchEntity'];
export type KeywordReserveReorderRequestContract = components['schemas']['KeywordReserveReorderRequest'];
export type TagUpsertRequestContract = components['schemas']['TagUpsertRequest'];
export type TagMergeRequestContract = components['schemas']['TagMergeRequest'];
export type RecordingMaintenanceRequestContract = components['schemas']['RecordingMaintenanceRequest'];
export type ExternalImportScanRequestContract = components['schemas']['ExternalImportScanRequest'];
export type ExternalImportCandidatesRequestContract = components['schemas']['ExternalImportCandidatesRequest'];
export type RecordingBulkDeleteRequestContract = components['schemas']['RecordingBulkDeleteRequest'];
export type RecordingBulkListenedRequestContract = components['schemas']['RecordingBulkListenedRequest'];
export type RecordingBulkTagRequestContract = components['schemas']['RecordingBulkTagRequest'];
export type EmptyRequestContract = Record<string, never>;
export type UpdateRecordDirectoryPathContract = components['schemas']['UpdateRecordDirectoryPathEntity'];
export type UpdateRecordFileNameTemplateContract = components['schemas']['UpdateRecordFileNameTemplateEntity'];
export type UpdateDurationContract = components['schemas']['UpdateDurationEntity'];
export type UpdateRadiruAreaContract = components['schemas']['UpdateRadiruAreaEntity'];
export type UpdateExternalServiceUserAgentContract = components['schemas']['UpdateExternalServiceUserAgentEntity'];
export type UpdateRadiruRequestSettingsContract = components['schemas']['UpdateRadiruRequestSettingsEntity'];
export type UpdateNotificationSettingContract = components['schemas']['UpdateNotificationSettingEntity'];
export type UpdateUnreadBadgeNoticeCategoriesContract = components['schemas']['UpdateUnreadBadgeNoticeCategoriesEntity'];
export type UpdateRadikoLoginContract = components['schemas']['UpdateRadikoLoginEntity'];
export type UpdateExternalImportTimeZoneContract = components['schemas']['UpdateExternalImportTimeZoneEntity'];
export type UpdateStorageLowSpaceThresholdContract = components['schemas']['UpdateStorageLowSpaceThresholdEntity'];
export type UpdateMonitoringAdvancedContract = components['schemas']['UpdateMonitoringAdvancedEntity'];
export type UpdateClockSkewMonitoringContract = components['schemas']['UpdateClockSkewMonitoringEntity'];
export type UpdateMergeTagsFromMatchedRulesContract = components['schemas']['UpdateMergeTagsFromMatchedRulesEntity'];
export type UpdateEmbedProgramImageOnRecordContract = components['schemas']['UpdateEmbedProgramImageOnRecordEntity'];
export type UpdateResumePlaybackAcrossPagesContract = components['schemas']['UpdateResumePlaybackAcrossPagesEntity'];
export type UpdateReleaseCheckIntervalContract = components['schemas']['UpdateReleaseCheckIntervalEntity'];
export type UpdateDuplicateDetectionIntervalContract = components['schemas']['UpdateDuplicateDetectionIntervalEntity'];
