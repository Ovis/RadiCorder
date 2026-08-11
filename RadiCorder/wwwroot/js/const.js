export const API_ENDPOINTS = {
    RADIO_DATE: `/api/general/radio-dates`,
    STATION_LIST_RADIKO: `/api/programs/stations/radiko`,
    STATION_LIST_RADIRU: `/api/programs/stations/radiru`,
    PROGRAM_DETAIL: `/api/programs/detail`,
    PROGRAM_SEARCH: `/api/programs/search`,
    PROGRAM_NOW: `/api/programs/now`,
    PROGRAM_RECORDED: `/api/recordings`,
    PROGRAM_RECORDED_STATIONS: `/api/recordings/stations`,
    PROGRAM_RECORDED_DUPLICATES_RUN: `/api/recordings/duplicates/run`,
    PROGRAM_RECORDED_DUPLICATES_STATUS: `/api/recordings/duplicates/status`,
    PROGRAM_RECORDED_DUPLICATES_CANDIDATES: `/api/recordings/duplicates/candidates`,
    TAGS: `/api/tags`,
    RECORDING_TAGS_BULK_ADD: `/api/recordings/tags/bulk-add`,
    RECORDING_TAGS_BULK_REMOVE: `/api/recordings/tags/bulk-remove`,
    RECORDING_LISTENED_BULK_UPDATE: `/api/recordings/listened/bulk`,
    PROGRAM_RESERVE: `/api/programs/reserve`,
    PROGRAM_LIST_RADIKO: `/api/programs/list/radiko`,
    PROGRAM_LIST_RADIRU: `/api/programs/list/radiru`,
    PROGRAM_PLAY: `/api/programs/play`,
    KEYWORD_RESERVE: `/api/programs/keyword-reserve`,
    RESERVE_PROGRAM_LIST: `/api/reserves/programs`,
    RESERVE_KEYWORD_LIST: `/api/reserves/keywords`,
    RESERVE_PROGRAM_DELETE: `/api/reserves/programs/delete`,
    RESERVE_SWITCH_STATUS: `/api/reserves/keywords/switch`,
    RESERVE_KEYWORD_REORDER: `/api/reserves/keywords/reorder`,
    UPDATE_KEYWORD_RESERVE: `/api/reserves/keywords/update`,
    DELETE_KEYWORD_RESERVE: `/api/reserves/keywords/delete`,
    DELETE_PROGRAM_BULK: `/api/recordings/bulk-delete`,
    DOWNLOAD_PROGRAM: `/api/recordings/download/`,
    SETTING_RECORD_DIC_PATH: `/api/settings/record-directory`,
    SETTING_RECORD_FILENAME_TEMPLATE: `/api/settings/record-filename`,
    SETTING_DURATION: `/api/settings/duration`,
    SETTING_RADIRU_AREA: `/api/settings/radiru-area`,
    SETTING_EXTERNAL_SERVICE_USER_AGENT: `/api/settings/external-service-user-agent`,
    SETTING_EXTERNAL_SERVICE_RADIRU_REQUEST: `/api/settings/external-service-radiru-request`,
    SETTING_NOTICE: `/api/settings/notice`,
    SETTING_UNREAD_BADGE_NOTICE_CATEGORIES: `/api/settings/unread-badge-notice-categories`,
    SETTING_RADIKO_LOGIN: `/api/settings/radiko-login`,
    SETTING_RADIKO_LOGOUT: `/api/settings/radiko-logout`,
    SETTING_RADIKO_AREA_REFRESH: `/api/settings/radiko-area/refresh`,
    SETTING_EXTERNAL_IMPORT_TIMEZONE: `/api/settings/external-import-timezone`,
    SETTING_STORAGE_LOW_SPACE_THRESHOLD: `/api/settings/storage-low-space-threshold`,
    SETTING_MONITORING_ADVANCED: `/api/settings/monitoring-advanced`,
    SETTING_CLOCK_SKEW_MONITORING: `/api/settings/clock-skew-monitoring`,
    SETTING_MERGE_TAGS_FROM_MATCHED_RULES: `/api/settings/merge-tags-from-matched-rules`,
    SETTING_EMBED_PROGRAM_IMAGE_ON_RECORD: `/api/settings/embed-program-image-on-record`,
    SETTING_RESUME_PLAYBACK_ACROSS_PAGES: `/api/settings/resume-playback-across-pages`,
    SETTING_RELEASE_CHECK_INTERVAL: `/api/settings/release-check-interval`,
    SETTING_DUPLICATE_DETECTION_INTERVAL: `/api/settings/duplicate-detection-interval`,
    EXTERNAL_IMPORT_SCAN: `/api/settings/external-import/scan`,
    EXTERNAL_IMPORT_EXPORT_CSV: `/api/settings/external-import/export-csv`,
    EXTERNAL_IMPORT_IMPORT_CSV: `/api/settings/external-import/import-csv`,
    EXTERNAL_IMPORT_SAVE: `/api/settings/external-import/save`,
    EXTERNAL_IMPORT_MAINTENANCE_SCAN_MISSING: `/api/settings/external-import/maintenance/scan-missing`,
    EXTERNAL_IMPORT_MAINTENANCE_RELINK_MISSING: `/api/settings/external-import/maintenance/relink-missing`,
    EXTERNAL_IMPORT_MAINTENANCE_DELETE_MISSING: `/api/settings/external-import/maintenance/delete-missing`,
    SETTING_PROGRAM_UPDATE: `/api/programs/update`,
    SETTING_PROGRAM_UPDATE_STATUS: `/api/programs/update-status`,
    NOTIFICATION_LIST: `/api/notifications`,
    NOTIFICATION_COUNT: `/api/notifications/count`,
    NOTIFICATION_UNREAD: `/api/notifications/latest`,
    NOTIFICATION_CLEAR: `/api/notifications/clear`,
};
// チェックボックスラベルのデータを配列として定義
export const DAYS_OF_WEEK = {
    1: { text: '日曜日', shortText: '日' },
    2: { text: '月曜日', shortText: '月' },
    4: { text: '火曜日', shortText: '火' },
    8: { text: '水曜日', shortText: '水' },
    16: { text: '木曜日', shortText: '木' },
    32: { text: '金曜日', shortText: '金' },
    64: { text: '土曜日', shortText: '土' }
};
export function getDayOfWeekShortString(days) {
    let dayStrings = [];
    for (let day of days) {
        if (DAYS_OF_WEEK[day]) {
            dayStrings.push(DAYS_OF_WEEK[day].shortText);
        }
        else {
            throw new Error(`無効な値: ${day}`);
        }
    }
    return dayStrings.join('、');
}
//# sourceMappingURL=const.js.map