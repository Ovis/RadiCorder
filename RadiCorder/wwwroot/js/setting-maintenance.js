import { API_ENDPOINTS } from './const.js';
import { showConfirmDialog } from './feedback.js';
import { withButtonLoading } from './loading.js';
export const initSettingMaintenance = (verificationToken, showToast) => {
    const panel = document.getElementById('setting-panel-maintenance');
    if (!panel) {
        return;
    }
    const maintenanceScanButton = document.getElementById('recording-maintenance-scan-btn');
    const maintenanceRelinkButton = document.getElementById('recording-maintenance-relink-btn');
    const maintenanceDeleteButton = document.getElementById('recording-maintenance-delete-btn');
    const maintenanceTableBody = document.getElementById('recording-maintenance-table-body');
    const maintenanceRowTemplate = document.getElementById('recording-maintenance-row-template');
    const maintenanceTotalCountLabel = document.getElementById('recording-maintenance-total-count');
    const maintenanceSelectAll = document.getElementById('recording-maintenance-select-all');
    if (!maintenanceScanButton || !maintenanceRelinkButton || !maintenanceDeleteButton
        || !maintenanceTableBody || !maintenanceRowTemplate || !maintenanceTotalCountLabel || !maintenanceSelectAll) {
        return;
    }
    let maintenanceEntries = [];
    let suppressNextMaintenanceSyncAction = null;
    const parseErrorResponse = async (response) => {
        const defaultMessage = `処理に失敗しました。（HTTP ${response.status}）`;
        try {
            const result = await response.json();
            if (result.message) {
                return result.message;
            }
        }
        catch {
            return defaultMessage;
        }
        return defaultMessage;
    };
    const updateMaintenanceButtons = (disabled) => {
        maintenanceScanButton.disabled = disabled;
        maintenanceRelinkButton.disabled = disabled;
        maintenanceDeleteButton.disabled = disabled;
    };
    const updateMaintenanceSelectAllState = () => {
        if (maintenanceEntries.length === 0) {
            maintenanceSelectAll.checked = false;
            maintenanceSelectAll.indeterminate = false;
            return;
        }
        const selectedCount = maintenanceEntries.filter(x => x.isSelected ?? true).length;
        maintenanceSelectAll.checked = selectedCount === maintenanceEntries.length;
        maintenanceSelectAll.indeterminate = selectedCount > 0 && selectedCount < maintenanceEntries.length;
    };
    const renderMaintenance = () => {
        maintenanceTableBody.innerHTML = '';
        maintenanceEntries.forEach((entry) => {
            const row = maintenanceRowTemplate.content.cloneNode(true);
            const checkbox = row.querySelector('.recording-maintenance-select');
            const title = row.querySelector('.recording-maintenance-title');
            const station = row.querySelector('.recording-maintenance-station');
            const path = row.querySelector('.recording-maintenance-path');
            const count = row.querySelector('.recording-maintenance-candidates-count');
            const candidatesCell = row.querySelector('.recording-maintenance-candidates');
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
    const selectedMaintenanceIds = () => {
        return maintenanceEntries
            .filter(x => x.isSelected ?? true)
            .map(x => x.recordingId);
    };
    const scanMaintenance = async (showSuccessToast = true, silentServer = false) => {
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
        const result = await response.json();
        maintenanceEntries = (result.data?.entries ?? []).map(x => ({ ...x, isSelected: true }));
        renderMaintenance();
        if (showSuccessToast) {
            showToast(result.message ?? '欠損レコードを抽出しました。');
        }
    };
    /**
     * 他画面/他タブのメンテナンス実行完了を受けて一覧を再同期する
     */
    const onOperationEvent = (event) => {
        const customEvent = event;
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
            }
            catch (error) {
                const message = error instanceof Error ? error.message : `${error}`;
                showToast(message, false);
            }
            finally {
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
                const requestBody = { recordingIds };
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
                await response.json();
                suppressNextMaintenanceSyncAction = 'relink-missing';
                await scanMaintenance(false, true);
            }
            catch (error) {
                const message = error instanceof Error ? error.message : `${error}`;
                showToast(message, false);
            }
            finally {
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
                const requestBody = { recordingIds };
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
                await response.json();
                suppressNextMaintenanceSyncAction = 'delete-missing';
                await scanMaintenance(false, true);
            }
            catch (error) {
                const message = error instanceof Error ? error.message : `${error}`;
                showToast(message, false);
            }
            finally {
                updateMaintenanceButtons(false);
            }
        }, { busyText: '削除中...' });
    });
    renderMaintenance();
    return () => window.removeEventListener('radicorder:operation-event', onOperationEvent);
};
//# sourceMappingURL=setting-maintenance.js.map