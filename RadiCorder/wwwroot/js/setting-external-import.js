import { API_ENDPOINTS } from './const.js';
import { withButtonLoading } from './loading.js';
const DEFAULT_PAGE_SIZE = 20;
export const initExternalImport = (verificationToken, showToast) => {
    const panel = document.getElementById('setting-panel-external-import');
    if (!panel) {
        return;
    }
    const scanButton = document.getElementById('external-import-scan-btn');
    const applyDefaultTagCheckbox = document.getElementById('external-import-apply-default-tag');
    const markAsListenedCheckbox = document.getElementById('external-import-mark-as-listened');
    const exportButton = document.getElementById('external-import-export-btn');
    const importButton = document.getElementById('external-import-import-btn');
    const importFileInput = document.getElementById('external-import-file-input');
    const saveButton = document.getElementById('external-import-save-btn');
    const tableBody = document.getElementById('external-import-table-body');
    const rowTemplate = document.getElementById('external-import-row-template');
    const paginationList = document.getElementById('external-import-pagination-list');
    const totalCountLabel = document.getElementById('external-import-total-count');
    const selectAllCheckbox = document.getElementById('external-import-select-all');
    if (!scanButton || !applyDefaultTagCheckbox || !markAsListenedCheckbox || !exportButton || !importButton || !importFileInput || !saveButton || !tableBody || !rowTemplate || !paginationList || !totalCountLabel || !selectAllCheckbox) {
        return;
    }
    const normalizeCandidate = (candidate) => ({
        isSelected: candidate.isSelected ?? false,
        filePath: candidate.filePath ?? '',
        title: candidate.title ?? '',
        description: candidate.description ?? '',
        stationName: candidate.stationName ?? '',
        broadcastAt: candidate.broadcastAt ?? '',
        tags: candidate.tags ?? []
    });
    let candidates = [];
    let currentPage = 1;
    const getPageItems = () => {
        const start = (currentPage - 1) * DEFAULT_PAGE_SIZE;
        return candidates.slice(start, start + DEFAULT_PAGE_SIZE);
    };
    const updateTotal = () => {
        totalCountLabel.textContent = `${candidates.length} 件`;
    };
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
    const updateSelectAllState = () => {
        const pageItems = getPageItems();
        if (pageItems.length === 0) {
            selectAllCheckbox.checked = false;
            selectAllCheckbox.indeterminate = false;
            return;
        }
        const selectedCount = pageItems.filter(x => x.isSelected).length;
        selectAllCheckbox.checked = selectedCount === pageItems.length;
        selectAllCheckbox.indeterminate = selectedCount > 0 && selectedCount < pageItems.length;
    };
    const renderPagination = () => {
        paginationList.innerHTML = '';
        const totalPages = Math.ceil(candidates.length / DEFAULT_PAGE_SIZE);
        if (totalPages <= 1) {
            return;
        }
        const createPageItem = (page, text) => {
            const li = document.createElement('li');
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'rk-page-btn';
            if (page === currentPage) {
                button.classList.add('is-current');
            }
            button.textContent = text ?? page.toString();
            button.addEventListener('click', () => {
                currentPage = page;
                renderTable();
            });
            li.appendChild(button);
            return li;
        };
        const prev = createPageItem(Math.max(1, currentPage - 1), '<');
        if (currentPage === 1) {
            prev.firstElementChild.disabled = true;
        }
        paginationList.appendChild(prev);
        for (let page = 1; page <= totalPages; page++) {
            paginationList.appendChild(createPageItem(page));
        }
        const next = createPageItem(Math.min(totalPages, currentPage + 1), '>');
        if (currentPage === totalPages) {
            next.firstElementChild.disabled = true;
        }
        paginationList.appendChild(next);
    };
    const renderTable = () => {
        tableBody.innerHTML = '';
        const pageItems = getPageItems();
        pageItems.forEach((candidate) => {
            const row = rowTemplate.content.cloneNode(true);
            const select = row.querySelector('.external-import-select');
            const filePath = row.querySelector('.external-import-file-path');
            const title = row.querySelector('.external-import-title');
            const description = row.querySelector('.external-import-description');
            const stationName = row.querySelector('.external-import-station-name');
            const broadcastAt = row.querySelector('.external-import-broadcast-at');
            const tags = row.querySelector('.external-import-tags');
            if (!select || !filePath || !title || !description || !stationName || !broadcastAt || !tags) {
                return;
            }
            select.checked = candidate.isSelected;
            filePath.value = candidate.filePath;
            title.value = candidate.title;
            description.value = candidate.description;
            stationName.value = candidate.stationName;
            broadcastAt.value = candidate.broadcastAt.substring(0, 16);
            tags.value = candidate.tags.join(', ');
            select.addEventListener('change', () => {
                candidate.isSelected = select.checked;
                updateSelectAllState();
            });
            title.addEventListener('input', () => {
                candidate.title = title.value;
            });
            description.addEventListener('input', () => {
                candidate.description = description.value;
            });
            stationName.addEventListener('input', () => {
                candidate.stationName = stationName.value;
            });
            broadcastAt.addEventListener('change', () => {
                const parsed = new Date(broadcastAt.value);
                if (Number.isNaN(parsed.getTime())) {
                    showToast('放送日時の形式が不正です。', false);
                    broadcastAt.value = candidate.broadcastAt.substring(0, 16);
                    return;
                }
                candidate.broadcastAt = parsed.toISOString();
            });
            tags.addEventListener('change', () => {
                candidate.tags = tags.value
                    .split(',')
                    .map(x => x.trim())
                    .filter(x => x.length > 0);
            });
            tableBody.appendChild(row);
        });
        updateSelectAllState();
        renderPagination();
        updateTotal();
    };
    const updateButtons = (disabled) => {
        scanButton.disabled = disabled;
        exportButton.disabled = disabled;
        importButton.disabled = disabled;
        saveButton.disabled = disabled;
    };
    scanButton.addEventListener('click', async () => {
        await withButtonLoading(scanButton, async () => {
            updateButtons(true);
            try {
                const requestBody = {
                    applyDefaultTag: applyDefaultTagCheckbox.checked
                };
                const response = await fetch(API_ENDPOINTS.EXTERNAL_IMPORT_SCAN, {
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
                const result = await response.json();
                candidates = (result.data ?? []).map(normalizeCandidate);
                currentPage = 1;
                renderTable();
                showToast(result.message ?? 'スキャンが完了しました。');
            }
            catch (error) {
                const message = error instanceof Error ? error.message : `${error}`;
                showToast(message, false);
            }
            finally {
                updateButtons(false);
            }
        }, { busyText: 'スキャン中...' });
    });
    exportButton.addEventListener('click', async () => {
        if (candidates.length === 0) {
            showToast('CSV出力する候補がありません。', false);
            return;
        }
        try {
            const requestBody = { candidates };
            const response = await fetch(API_ENDPOINTS.EXTERNAL_IMPORT_EXPORT_CSV, {
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
            const blob = await response.blob();
            const url = URL.createObjectURL(blob);
            const anchor = document.createElement('a');
            anchor.href = url;
            anchor.download = `external-import-${new Date().toISOString().replace(/[-:]/g, '').slice(0, 14)}.csv`;
            anchor.click();
            URL.revokeObjectURL(url);
            showToast('CSVをダウンロードしました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    importButton.addEventListener('click', async () => {
        const file = importFileInput.files?.[0];
        if (!file) {
            showToast('CSVファイルを選択してください。', false);
            return;
        }
        await withButtonLoading(importButton, async () => {
            updateButtons(true);
            try {
                const formData = new FormData();
                formData.append('file', file);
                const response = await fetch(API_ENDPOINTS.EXTERNAL_IMPORT_IMPORT_CSV, {
                    method: 'POST',
                    headers: { 'RequestVerificationToken': verificationToken },
                    body: formData
                });
                if (!response.ok) {
                    throw new Error(await parseErrorResponse(response));
                }
                const result = await response.json();
                candidates = (result.data ?? []).map(normalizeCandidate);
                currentPage = 1;
                renderTable();
                importFileInput.value = '';
                showToast(result.message ?? 'CSVを反映しました。');
            }
            catch (error) {
                const message = error instanceof Error ? error.message : `${error}`;
                showToast(message, false);
            }
            finally {
                updateButtons(false);
            }
        }, { busyText: '取込中...' });
    });
    saveButton.addEventListener('click', async () => {
        if (candidates.length === 0) {
            showToast('保存対象がありません。', false);
            return;
        }
        await withButtonLoading(saveButton, async () => {
            updateButtons(true);
            try {
                const requestBody = {
                    candidates,
                    markAsListened: markAsListenedCheckbox.checked
                };
                const response = await fetch(API_ENDPOINTS.EXTERNAL_IMPORT_SAVE, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': verificationToken
                    },
                    body: JSON.stringify(requestBody)
                });
                const result = await response.json();
                if (!response.ok || !result.success) {
                    const details = result.data?.errors?.slice(0, 5).map(x => `${x.filePath}: ${x.message}`).join('\n');
                    showToast(details ?? (result.message ?? '保存に失敗しました。'), false);
                    return;
                }
                candidates = [];
                currentPage = 1;
                renderTable();
            }
            catch (error) {
                const message = error instanceof Error ? error.message : `${error}`;
                showToast(message, false);
            }
            finally {
                updateButtons(false);
            }
        }, { busyText: '保存中...' });
    });
    selectAllCheckbox.addEventListener('change', () => {
        const checked = selectAllCheckbox.checked;
        getPageItems().forEach((item) => {
            item.isSelected = checked;
        });
        renderTable();
    });
    // 取込タブ選択前にもテーブル初期状態を描画しておく
    renderTable();
};
//# sourceMappingURL=setting-external-import.js.map