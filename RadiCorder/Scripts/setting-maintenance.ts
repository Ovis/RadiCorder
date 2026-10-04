import {
    ApiResponseContract as ApiResponse,
    RecordingFileMaintenanceActionResultResponseContract as RecordingFileMaintenanceActionResult,
    RecordingFileMaintenanceEntryResponseContract as RecordingFileMaintenanceEntry,
    RecordingFileMaintenanceScanResultResponseContract as RecordingFileMaintenanceScanResult
} from './openapi-response-contract.js';
import { API_ENDPOINTS } from './const.js';
import type { RecordingMaintenanceRequestContract } from './openapi-contract.js';
import { showConfirmDialog } from './feedback.js';
import { withButtonLoading } from './loading.js';

type ShowToastFn = (message: string, isSuccess?: boolean) => void;
type AppOperationEventDetail = {
    category: string;
    action: string;
    succeeded: boolean;
    message: string;
};

export const initSettingMaintenance = (verificationToken: string, showToast: ShowToastFn): (() => void) | undefined => {
    const panel = document.getElementById('setting-panel-maintenance') as HTMLDivElement | null;
    if (!panel) {
        return;
    }

    const maintenanceScanButton = document.getElementById('recording-maintenance-scan-btn') as HTMLButtonElement | null;
    const maintenanceRelinkButton = document.getElementById('recording-maintenance-relink-btn') as HTMLButtonElement | null;
    const maintenanceDeleteButton = document.getElementById('recording-maintenance-delete-btn') as HTMLButtonElement | null;
    const maintenanceTableBody = document.getElementById('recording-maintenance-table-body') as HTMLTableSectionElement | null;
    const maintenanceRowTemplate = document.getElementById('recording-maintenance-row-template') as HTMLTemplateElement | null;
    const maintenanceTotalCountLabel = document.getElementById('recording-maintenance-total-count') as HTMLSpanElement | null;
    const maintenanceSelectAll = document.getElementById('recording-maintenance-select-all') as HTMLInputElement | null;

    if (!maintenanceScanButton || !maintenanceRelinkButton || !maintenanceDeleteButton
        || !maintenanceTableBody || !maintenanceRowTemplate || !maintenanceTotalCountLabel || !maintenanceSelectAll) {
        return;
    }

    let maintenanceEntries: RecordingFileMaintenanceEntry[] = [];
    let suppressNextMaintenanceSyncAction: string | null = null;

    const parseErrorResponse = async (response: Response): Promise<string> => {
        const defaultMessage = `処理に失敗しました。（HTTP ${response.status}）`;
        try {
            const result = await response.json() as ApiResponse<null>;
            if (result.message) {
                return result.message;
            }
        } catch {
            return defaultMessage;
        }

        return defaultMessage;
    };

    const updateMaintenanceButtons = (disabled: boolean): void => {
        maintenanceScanButton.disabled = disabled;
        maintenanceRelinkButton.disabled = disabled;
        maintenanceDeleteButton.disabled = disabled;
    };

    const updateMaintenanceSelectAllState = (): void => {
        if (maintenanceEntries.length === 0) {
            maintenanceSelectAll.checked = false;
            maintenanceSelectAll.indeterminate = false;
            return;
        }

        const selectedCount = maintenanceEntries.filter(x => x.isSelected ?? true).length;
        maintenanceSelectAll.checked = selectedCount === maintenanceEntries.length;
        maintenanceSelectAll.indeterminate = selectedCount > 0 && selectedCount < maintenanceEntries.length;
    };

    const renderMaintenance = (): void => {
        maintenanceTableBody.innerHTML = '';
        maintenanceEntries.forEach((entry) => {
            const row = maintenanceRowTemplate.content.cloneNode(true) as HTMLElement;
            const checkbox = row.querySelector('.recording-maintenance-select') as HTMLInputElement | null;
            const title = row.querySelector('.recording-maintenance-title') as HTMLTableCellElement | null;
            const station = row.querySelector('.recording-maintenance-station') as HTMLTableCellElement | null;
            const path = row.querySelector('.recording-maintenance-path') as HTMLTableCellElement | null;
            const count = row.querySelector('.recording-maintenance-candidates-count') as HTMLTableCellElement | null;
            const candidatesCell = row.querySelector('.recording-maintenance-candidates') as HTMLTableCellElement | null;

            if (!checkbox || !title || !station || !path || !count || !candidatesCell) {
                return;
            }

            checkbox.checked = entry.isSelected ?? true;
            checkbox.addEventListener('change', () => {
                entry.isSelected = checkbox.checked;
                updateMaintenanceSelectAllState();
            });

            title.textContent = entry.title;
            station.textContent = entry.stationName;
            path.textContent = entry.storedPath;
            count.textContent = `${entry.candidateCount}`;
            candidatesCell.textContent = entry.candidateRelativePaths.length > 0
                ? entry.candidateRelativePaths.join(' / ')
                : '候補なし';

            maintenanceTableBody.appendChild(row);
        });

        maintenanceTotalCountLabel.textContent = `${maintenanceEntries.length} 件`;
        updateMaintenanceSelectAllState();
    };

    const selectedMaintenanceIds = (): string[] => {
        return maintenanceEntries
            .filter(x => x.isSelected ?? true)
            .map(x => x.recordingId);
    };

    const scanMaintenance = async (showSuccessToast = true, silentServer = false): Promise<void> => {
        const endpoint = silentServer
            ? `${API_ENDPOINTS.EXTERNAL_IMPORT_MAINTENANCE_SCAN_MISSING}?silent=true`
            : API_ENDPOINTS.EXTERNAL_IMPORT_MAINTENANCE_SCAN_MISSING;

        const response = await fetch(endpoint, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': verificationToken
            }
        });
        if (!response.ok) {
            throw new Error(await parseErrorResponse(response));
        }

        const result = await response.json() as ApiResponse<RecordingFileMaintenanceScanResult>;
        maintenanceEntries = (result.data?.entries ?? []).map(x => ({ ...x, isSelected: true }));
        renderMaintenance();
        if (showSuccessToast) {
            showToast(result.message ?? '欠損レコードを抽出しました。');
        }
    };

    /**
     * 他画面/他タブのメンテナンス実行完了を受けて一覧を再同期する
     */
    const onOperationEvent = (event: Event) => {
        const customEvent = event as CustomEvent<AppOperationEventDetail>;
        const detail = customEvent.detail;
        if (!detail || detail.category !== 'maintenance' || !detail.succeeded) {
            return;
        }

        if (detail.action !== 'relink-missing' && detail.action !== 'delete-missing') {
            return;
        }

        if (suppressNextMaintenanceSyncAction === detail.action) {
            suppressNextMaintenanceSyncAction = null;
            return;
        }

        void scanMaintenance(false, true).catch(() => {
            // 再同期失敗時は明示トーストを追加しない
        });
    };
    window.addEventListener('radicorder:operation-event', onOperationEvent);

    maintenanceSelectAll.addEventListener('change', () => {
        const checked = maintenanceSelectAll.checked;
        maintenanceEntries.forEach((item) => {
            item.isSelected = checked;
        });
        renderMaintenance();
    });

    maintenanceScanButton.addEventListener('click', async () => {
        await withButtonLoading(maintenanceScanButton, async () => {
            updateMaintenanceButtons(true);
            try {
                await scanMaintenance(false, false);
            } catch (error) {
                const message = error instanceof Error ? error.message : `${error}`;
                showToast(message, false);
            } finally {
                updateMaintenanceButtons(false);
            }
        }, { busyText: '抽出中...' });
    });

    maintenanceRelinkButton.addEventListener('click', async () => {
        const recordingIds = selectedMaintenanceIds();
        if (recordingIds.length === 0) {
            showToast('対象レコードを選択してください。', false);
            return;
        }

        await withButtonLoading(maintenanceRelinkButton, async () => {
            updateMaintenanceButtons(true);
            try {
                const requestBody: RecordingMaintenanceRequestContract = { recordingIds };
                const response = await fetch(API_ENDPOINTS.EXTERNAL_IMPORT_MAINTENANCE_RELINK_MISSING, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': verificationToken
                    },
                    body: JSON.stringify(requestBody)
                });
                if (!response.ok) {
                    throw new Error(await parseErrorResponse(response));
                }

                await response.json() as ApiResponse<RecordingFileMaintenanceActionResult>;
                suppressNextMaintenanceSyncAction = 'relink-missing';
                await scanMaintenance(false, true);
            } catch (error) {
                const message = error instanceof Error ? error.message : `${error}`;
                showToast(message, false);
            } finally {
                updateMaintenanceButtons(false);
            }
        }, { busyText: '再紐付け中...' });
    });

    maintenanceDeleteButton.addEventListener('click', async () => {
        const recordingIds = selectedMaintenanceIds();
        if (recordingIds.length === 0) {
            showToast('対象レコードを選択してください。', false);
            return;
        }

        const confirmed = await showConfirmDialog(`${recordingIds.length}件の欠損レコードをDBから削除します。\nこの操作は元に戻せません。`, {
            title: '欠損レコード削除',
            okText: '削除する'
        });
        if (!confirmed) {
            return;
        }

        await withButtonLoading(maintenanceDeleteButton, async () => {
            updateMaintenanceButtons(true);
            try {
                const requestBody: RecordingMaintenanceRequestContract = { recordingIds };
                const response = await fetch(API_ENDPOINTS.EXTERNAL_IMPORT_MAINTENANCE_DELETE_MISSING, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': verificationToken
                    },
                    body: JSON.stringify(requestBody)
                });
                if (!response.ok) {
                    throw new Error(await parseErrorResponse(response));
                }

                await response.json() as ApiResponse<RecordingFileMaintenanceActionResult>;
                suppressNextMaintenanceSyncAction = 'delete-missing';
                await scanMaintenance(false, true);
            } catch (error) {
                const message = error instanceof Error ? error.message : `${error}`;
                showToast(message, false);
            } finally {
                updateMaintenanceButtons(false);
            }
        }, { busyText: '削除中...' });
    });

    renderMaintenance();
    return () => window.removeEventListener('radicorder:operation-event', onOperationEvent);
};

