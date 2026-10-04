import { API_ENDPOINTS } from './const.js';
import { showConfirmDialog, showGlobalToast } from './feedback.js';
import { withButtonLoading } from './loading.js';
import { setTextContent, setEventListener, formatDisplayDateTime } from './utils.js';
import { playerPlaybackRateOptions } from './player-rate-control.js';
import { createStandardPlayerJumpControls } from './player-jump-controls.js';
import { configurePlayer, finishPlayer, playPlayerSource, stopPlayer } from './player-controller.js';
import { registerPage } from './page-navigation.js';
import { clearMultiSelect, renderSelectedTagChips, enableTouchLikeMultiSelect } from './tag-select-ui.js';
const sortingElements = {
    'sort-title': 'Title',
    'sort-start': 'StartDateTime',
    'sort-duration': 'Duration'
};
let currentPage = 1;
const defaultPageSize = 10;
let currentPageSize = defaultPageSize;
let sortBy = "StartDateTime";
let isDescending = true;
let searchQuery = "";
let withinDays = null;
let stationIdFilter = "";
let selectedTagIds = [];
let tagMode = "or";
let untaggedOnly = false;
let unlistenedOnly = false;
const selectedRecordingIds = new Set();
let lastLoadedRecordings = [];
const continuousPlaybackStorageKey = 'radicorder-recorded-continuous-playback';
let currentPlayingRecordingId = null;
let recordingHubConnection = null;
let isRealtimeReloadRunning = false;
let hasRealtimeReloadPending = false;
let activePageSignal = null;
function isCurrentRecordingPlaying(recordId) {
    return currentPlayingRecordingId === recordId;
}
function setRecordedPlaybackButtonState(buttonElm, isPlaying) {
    const iconElm = buttonElm.querySelector('i');
    if (isPlaying) {
        buttonElm.setAttribute('title', '停止');
        buttonElm.setAttribute('aria-label', '停止');
        buttonElm.classList.remove('is-primary');
        buttonElm.classList.add('is-danger');
        iconElm?.classList.remove('fa-play');
        iconElm?.classList.add('fa-stop');
        return;
    }
    buttonElm.setAttribute('title', '再生');
    buttonElm.setAttribute('aria-label', '再生');
    buttonElm.classList.remove('is-danger');
    buttonElm.classList.add('is-primary');
    iconElm?.classList.remove('fa-stop');
    iconElm?.classList.add('fa-play');
}
function syncRecordedListPlaybackButtons() {
    const playerButtons = document.querySelectorAll('.player-button[data-recording-id]');
    playerButtons.forEach((buttonElm) => {
        const recordingId = buttonElm.dataset.recordingId ?? '';
        setRecordedPlaybackButtonState(buttonElm, isCurrentRecordingPlaying(recordingId));
    });
}
function stopCurrentPlayback() {
    stopPlayer();
}
function clearRecordingSelection() {
    selectedRecordingIds.clear();
    const selectAllCheckbox = document.getElementById('recordings-select-all');
    if (selectAllCheckbox) {
        selectAllCheckbox.checked = false;
    }
}
function getSortSelectValue(sortKey, desc) {
    return `${sortKey}_${desc ? 'desc' : 'asc'}`;
}
function applySortSelectValue(value) {
    const [sortKey, order] = value.split('_');
    if (!sortKey || !order) {
        return;
    }
    sortBy = sortKey;
    isDescending = order === 'desc';
}
function normalizeTagName(value) {
    return value.trim().toLocaleLowerCase();
}
/**
 * SignalR経由の変更通知を受けた際に録音一覧を再同期する
 */
async function reloadRecordingsFromRealtimeAsync() {
    if (isRealtimeReloadRunning) {
        hasRealtimeReloadPending = true;
        return;
    }
    isRealtimeReloadRunning = true;
    try {
        do {
            hasRealtimeReloadPending = false;
            await loadRecordings(currentPage, sortBy, isDescending, searchQuery);
        } while (hasRealtimeReloadPending);
    }
    finally {
        isRealtimeReloadRunning = false;
    }
}
/**
 * 録音状態変更のSignalR接続を初期化する
 */
async function initializeRecordingHubConnectionAsync() {
    const signalRNamespace = window.signalR;
    if (!signalRNamespace) {
        console.warn('SignalRクライアントが読み込まれていないため、録音Push同期を無効化します。');
        return;
    }
    const connection = new signalRNamespace.HubConnectionBuilder()
        .withUrl('/hubs/recordings')
        .withAutomaticReconnect()
        .configureLogging(signalRNamespace.LogLevel.Warning)
        .build();
    connection.on('recordingStateChanged', () => {
        void reloadRecordingsFromRealtimeAsync();
    });
    connection.onreconnected(() => {
        void reloadRecordingsFromRealtimeAsync();
    });
    connection.onclose((error) => {
        if (error) {
            console.warn('SignalR接続が切断されました。', error);
        }
    });
    try {
        await connection.start();
        recordingHubConnection = connection;
    }
    catch (error) {
        console.warn('SignalR接続の開始に失敗しました。', error);
    }
}
let tagsModalElement = null;
let tagsModalTitleElement = null;
let tagsModalListElement = null;
registerPage('recorded.js', async (signal) => {
    activePageSignal = signal;
    currentPage = 1;
    currentPageSize = defaultPageSize;
    sortBy = 'StartDateTime';
    isDescending = true;
    searchQuery = '';
    withinDays = null;
    stationIdFilter = '';
    selectedTagIds = [];
    tagMode = 'or';
    untaggedOnly = false;
    unlistenedOnly = false;
    selectedRecordingIds.clear();
    lastLoadedRecordings = [];
    configurePlayer({
        createControls: createPlayerJumpControls,
        onEnded: () => { void handlePlaybackEnded(); },
        onStateChanged: (state) => {
            currentPlayingRecordingId = state?.recordId ?? null;
            syncRecordedListPlaybackButtons();
        }
    });
    const verificationToken = document.getElementById('VerificationToken')?.value ?? '';
    const searchBtn = document.getElementById('search-button');
    const pageSizeSelect = document.getElementById('page-size-select');
    const withinDaysSelect = document.getElementById('recorded-within-days-select');
    const stationSelect = document.getElementById('recorded-station-select');
    const tagFilterSelect = document.getElementById('recorded-tag-filter');
    const tagBulkSelect = document.getElementById('recorded-tag-bulk');
    const tagModeSelect = document.getElementById('recorded-tag-mode');
    const untaggedOnlyCheckbox = document.getElementById('recorded-untagged-only');
    const unlistenedOnlyCheckbox = document.getElementById('recorded-unlistened-only');
    const selectAllCheckbox = document.getElementById('recordings-select-all');
    const bulkAddButton = document.getElementById('bulk-tag-add-button');
    const bulkRemoveButton = document.getElementById('bulk-tag-remove-button');
    const bulkMarkListenedButton = document.getElementById('bulk-mark-listened-button');
    const bulkMarkUnlistenedButton = document.getElementById('bulk-mark-unlistened-button');
    const bulkDeleteButton = document.getElementById('bulk-delete-button');
    const tagFilterChipsContainer = document.getElementById('recorded-tag-filter-chips');
    const tagBulkChipsContainer = document.getElementById('recorded-tag-bulk-chips');
    const tagFilterClearButton = document.getElementById('recorded-tag-filter-clear');
    const tagBulkClearButton = document.getElementById('recorded-tag-bulk-clear');
    const tagBulkCreateInput = document.getElementById('recorded-tag-bulk-create-name');
    const tagBulkCreateButton = document.getElementById('recorded-tag-bulk-create-btn');
    const tagBulkCreateSuggestions = document.getElementById('recorded-tag-bulk-create-suggestions');
    const tagToolsToggleButton = document.getElementById('recorded-tag-tools-toggle');
    const tagToolsContent = document.getElementById('recorded-tag-tools-content');
    const tagModeTabs = Array.from(document.querySelectorAll('.recorded-tag-mode-tab'));
    const tagModePanels = Array.from(document.querySelectorAll('.recorded-tag-panel'));
    const mobileSortSelect = document.getElementById('mobile-sort-select');
    tagsModalElement = document.getElementById('recording-tags-modal');
    tagsModalTitleElement = document.getElementById('recording-tags-modal-title');
    tagsModalListElement = document.getElementById('recording-tags-modal-list');
    const tagsModalCloseButton = document.getElementById('recording-tags-modal-close');
    const tagsModalCloseFooterButton = document.getElementById('recording-tags-modal-close-footer');
    const tagsModalBackground = document.querySelector('#recording-tags-modal .modal-background');
    let previousMobileView = isMobileView();
    const recordedTagChipOptions = {
        emptyClassName: 'selected-tags-empty',
        chipClassName: 'selected-tag-chip'
    };
    enableTouchLikeMultiSelect(tagFilterSelect);
    enableTouchLikeMultiSelect(tagBulkSelect);
    if (tagToolsToggleButton && tagToolsContent) {
        tagToolsToggleButton.addEventListener('click', () => {
            const isOpen = tagToolsContent.classList.toggle('is-open');
            tagToolsToggleButton.classList.toggle('is-open', isOpen);
            tagToolsToggleButton.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
        });
    }
    if (tagModeTabs.length > 0 && tagModePanels.length > 0) {
        tagModeTabs.forEach((tab) => {
            tab.addEventListener('click', () => {
                const targetPanelId = tab.dataset.targetPanel;
                if (!targetPanelId) {
                    return;
                }
                tagModeTabs.forEach((item) => {
                    const isActive = item === tab;
                    item.classList.toggle('is-active', isActive);
                    item.setAttribute('aria-selected', isActive ? 'true' : 'false');
                });
                tagModePanels.forEach((panel) => {
                    panel.classList.toggle('is-active', panel.id === targetPanelId);
                });
            });
        });
    }
    if (tagsModalCloseButton) {
        tagsModalCloseButton.addEventListener('click', closeTagsModal);
    }
    if (tagsModalCloseFooterButton) {
        tagsModalCloseFooterButton.addEventListener('click', closeTagsModal);
    }
    if (tagsModalBackground) {
        tagsModalBackground.addEventListener('click', closeTagsModal);
    }
    if (pageSizeSelect) {
        pageSizeSelect.value = defaultPageSize.toString();
        pageSizeSelect.addEventListener('change', () => {
            const selectedSize = parseInt(pageSizeSelect.value, 10);
            currentPageSize = Number.isFinite(selectedSize) && selectedSize > 0 ? selectedSize : defaultPageSize;
            currentPage = 1;
            clearRecordingSelection();
            loadRecordings(currentPage, sortBy, isDescending, searchQuery);
        });
    }
    if (mobileSortSelect) {
        mobileSortSelect.value = getSortSelectValue(sortBy, isDescending);
        mobileSortSelect.addEventListener('change', () => {
            applySortSelectValue(mobileSortSelect.value);
            currentPage = 1;
            clearRecordingSelection();
            loadRecordings(currentPage, sortBy, isDescending, searchQuery);
        });
    }
    if (withinDaysSelect) {
        withinDaysSelect.addEventListener('change', () => {
            const selectedSize = parseInt(withinDaysSelect.value, 10);
            withinDays = Number.isFinite(selectedSize) && selectedSize > 0 ? selectedSize : null;
            currentPage = 1;
            clearRecordingSelection();
            loadRecordings(currentPage, sortBy, isDescending, searchQuery);
        });
    }
    if (stationSelect) {
        stationSelect.addEventListener('change', () => {
            stationIdFilter = stationSelect.value;
            currentPage = 1;
            clearRecordingSelection();
            loadRecordings(currentPage, sortBy, isDescending, searchQuery);
        });
    }
    if (tagFilterSelect) {
        tagFilterSelect.addEventListener('change', () => {
            selectedTagIds = Array.from(tagFilterSelect.selectedOptions).map(option => option.value);
            renderSelectedTagChips(tagFilterSelect, tagFilterChipsContainer, recordedTagChipOptions);
            currentPage = 1;
            clearRecordingSelection();
            loadRecordings(currentPage, sortBy, isDescending, searchQuery);
        });
    }
    if (tagFilterClearButton && tagFilterSelect) {
        tagFilterClearButton.addEventListener('click', () => {
            clearMultiSelect(tagFilterSelect);
            selectedTagIds = [];
            renderSelectedTagChips(tagFilterSelect, tagFilterChipsContainer, recordedTagChipOptions);
            currentPage = 1;
            clearRecordingSelection();
            loadRecordings(currentPage, sortBy, isDescending, searchQuery);
        });
    }
    if (tagModeSelect) {
        tagModeSelect.addEventListener('change', () => {
            tagMode = tagModeSelect.value;
            currentPage = 1;
            clearRecordingSelection();
            loadRecordings(currentPage, sortBy, isDescending, searchQuery);
        });
    }
    if (untaggedOnlyCheckbox) {
        untaggedOnlyCheckbox.addEventListener('change', () => {
            untaggedOnly = untaggedOnlyCheckbox.checked;
            currentPage = 1;
            clearRecordingSelection();
            loadRecordings(currentPage, sortBy, isDescending, searchQuery);
        });
    }
    if (unlistenedOnlyCheckbox) {
        unlistenedOnlyCheckbox.addEventListener('change', () => {
            unlistenedOnly = unlistenedOnlyCheckbox.checked;
            currentPage = 1;
            clearRecordingSelection();
            loadRecordings(currentPage, sortBy, isDescending, searchQuery);
        });
    }
    if (selectAllCheckbox) {
        selectAllCheckbox.addEventListener('change', () => {
            const checkboxes = document.querySelectorAll('input.recording-select');
            checkboxes.forEach((checkbox) => {
                checkbox.checked = selectAllCheckbox.checked;
                const id = checkbox.dataset.recordingId;
                if (!id) {
                    return;
                }
                if (checkbox.checked) {
                    selectedRecordingIds.add(id);
                }
                else {
                    selectedRecordingIds.delete(id);
                }
            });
        });
    }
    if (bulkAddButton) {
        bulkAddButton.addEventListener('click', async () => {
            await withButtonLoading(bulkAddButton, async () => {
                const tagIds = Array.from(tagBulkSelect.selectedOptions).map(option => option.value);
                await executeBulkTagOperation(API_ENDPOINTS.RECORDING_TAGS_BULK_ADD, tagIds, "タグを追加しました。", verificationToken);
            }, { busyText: 'タグ追加中...' });
        });
    }
    if (bulkRemoveButton) {
        bulkRemoveButton.addEventListener('click', async () => {
            await withButtonLoading(bulkRemoveButton, async () => {
                const tagIds = Array.from(tagBulkSelect.selectedOptions).map(option => option.value);
                await executeBulkTagOperation(API_ENDPOINTS.RECORDING_TAGS_BULK_REMOVE, tagIds, "タグを解除しました。", verificationToken);
            }, { busyText: 'タグ解除中...' });
        });
    }
    if (bulkDeleteButton) {
        bulkDeleteButton.addEventListener('click', async () => {
            await withButtonLoading(bulkDeleteButton, async () => {
                await executeBulkDeleteOperation(verificationToken);
            }, { busyText: '削除中...' });
        });
    }
    if (bulkMarkListenedButton) {
        bulkMarkListenedButton.addEventListener('click', async () => {
            await withButtonLoading(bulkMarkListenedButton, async () => {
                await executeBulkListenedOperation(true, verificationToken);
            }, { busyText: '視聴状態更新中...' });
        });
    }
    if (bulkMarkUnlistenedButton) {
        bulkMarkUnlistenedButton.addEventListener('click', async () => {
            await withButtonLoading(bulkMarkUnlistenedButton, async () => {
                await executeBulkListenedOperation(false, verificationToken);
            }, { busyText: '視聴状態更新中...' });
        });
    }
    if (tagBulkSelect) {
        tagBulkSelect.addEventListener('change', () => {
            renderSelectedTagChips(tagBulkSelect, tagBulkChipsContainer, recordedTagChipOptions);
        });
    }
    if (tagBulkClearButton && tagBulkSelect) {
        tagBulkClearButton.addEventListener('click', () => {
            clearMultiSelect(tagBulkSelect);
            renderSelectedTagChips(tagBulkSelect, tagBulkChipsContainer, recordedTagChipOptions);
        });
    }
    const selectBulkTagById = (tagId) => {
        Array.from(tagBulkSelect.options).forEach((option) => {
            if (option.value === tagId) {
                option.selected = true;
            }
        });
        tagBulkSelect.dispatchEvent(new Event('change'));
    };
    const renderBulkTagCreateSuggestions = () => {
        if (!tagBulkCreateInput || !tagBulkCreateSuggestions) {
            return;
        }
        tagBulkCreateSuggestions.innerHTML = '';
        const keyword = tagBulkCreateInput.value.trim();
        if (!keyword) {
            return;
        }
        const normalizedKeyword = normalizeTagName(keyword);
        const options = Array.from(tagBulkSelect.options);
        const exact = options.find((option) => normalizeTagName(option.textContent ?? '') === normalizedKeyword);
        if (!exact) {
            const createButton = document.createElement('button');
            createButton.type = 'button';
            createButton.className = 'button is-small is-info is-light';
            createButton.textContent = `「${keyword}」を新規作成`;
            createButton.addEventListener('click', () => {
                tagBulkCreateButton?.click();
            });
            tagBulkCreateSuggestions.appendChild(createButton);
        }
        options
            .filter((option) => (option.textContent ?? '').includes(keyword))
            .slice(0, 8)
            .forEach((option) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'button is-small is-light';
            button.textContent = option.textContent ?? option.value;
            button.addEventListener('click', () => {
                selectBulkTagById(option.value);
                if (tagBulkCreateInput) {
                    tagBulkCreateInput.value = '';
                }
                tagBulkCreateSuggestions.innerHTML = '';
                showGlobalToast(`タグ「${option.textContent ?? option.value}」を選択しました。`);
            });
            tagBulkCreateSuggestions.appendChild(button);
        });
    };
    if (tagBulkCreateInput) {
        tagBulkCreateInput.addEventListener('input', renderBulkTagCreateSuggestions);
        tagBulkCreateInput.addEventListener('keydown', (event) => {
            if (event.key === 'Enter') {
                event.preventDefault();
            }
        });
    }
    if (tagBulkCreateButton && tagBulkCreateInput) {
        tagBulkCreateButton.addEventListener('click', async () => {
            const name = tagBulkCreateInput.value.trim();
            if (!name) {
                showGlobalToast('タグ名を入力してください。', false);
                return;
            }
            const normalizedName = normalizeTagName(name);
            const existing = Array.from(tagBulkSelect.options)
                .find((option) => normalizeTagName(option.textContent ?? '') === normalizedName);
            if (existing) {
                selectBulkTagById(existing.value);
                tagBulkCreateInput.value = '';
                renderBulkTagCreateSuggestions();
                showGlobalToast(`タグ「${existing.textContent ?? name}」を選択しました。`);
                return;
            }
            const confirmed = await showConfirmDialog(`タグ「${name}」を作成しますか？`, { okText: '作成する' });
            if (!confirmed) {
                return;
            }
            await withButtonLoading(tagBulkCreateButton, async () => {
                try {
                    const selectedFilterTagIds = Array.from(tagFilterSelect.selectedOptions).map(option => option.value);
                    const selectedBulkTagIds = Array.from(tagBulkSelect.selectedOptions).map(option => option.value);
                    const requestBody = { name };
                    const response = await fetch(API_ENDPOINTS.TAGS, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(requestBody)
                    });
                    const result = await response.json();
                    if (!response.ok || !result.success) {
                        showGlobalToast(result.message ?? 'タグ作成に失敗しました。', false);
                        return;
                    }
                    const createdTagId = result.data?.id ?? '';
                    const filterSelectedSet = new Set(selectedFilterTagIds);
                    const bulkSelectedSet = new Set(selectedBulkTagIds);
                    if (createdTagId) {
                        bulkSelectedSet.add(createdTagId);
                    }
                    await loadTags();
                    Array.from(tagFilterSelect.options).forEach((option) => {
                        option.selected = filterSelectedSet.has(option.value);
                    });
                    Array.from(tagBulkSelect.options).forEach((option) => {
                        option.selected = bulkSelectedSet.has(option.value);
                    });
                    selectedTagIds = Array.from(tagFilterSelect.selectedOptions).map(option => option.value);
                    renderSelectedTagChips(tagFilterSelect, tagFilterChipsContainer, recordedTagChipOptions);
                    renderSelectedTagChips(tagBulkSelect, tagBulkChipsContainer, recordedTagChipOptions);
                    tagBulkCreateInput.value = '';
                    renderBulkTagCreateSuggestions();
                    showGlobalToast(result.message ?? 'タグを作成しました。');
                }
                catch (error) {
                    console.error('タグ作成に失敗しました。', error);
                    showGlobalToast('タグ作成に失敗しました。', false);
                }
            }, { busyText: '作成中...' });
        });
    }
    searchBtn.addEventListener('click', () => {
        const searchInput = document.getElementById('search-input');
        searchQuery = searchInput.value;
        currentPage = 1;
        clearRecordingSelection();
        loadRecordings(currentPage, sortBy, isDescending, searchQuery);
    });
    // テーブルヘッダークリックでソート処理
    Object.entries(sortingElements).forEach(([elementId, sortKey]) => {
        const element = document.getElementById(elementId);
        element.addEventListener('click', () => {
            sortBy = sortKey;
            isDescending = !isDescending;
            if (mobileSortSelect) {
                mobileSortSelect.value = getSortSelectValue(sortBy, isDescending);
            }
            currentPage = 1;
            clearRecordingSelection();
            loadRecordings(currentPage, sortBy, isDescending, searchQuery);
        });
    });
    await loadStationFilters();
    if (signal.aborted) {
        return;
    }
    await loadTags();
    if (signal.aborted) {
        return;
    }
    renderSelectedTagChips(tagFilterSelect, tagFilterChipsContainer, recordedTagChipOptions);
    renderSelectedTagChips(tagBulkSelect, tagBulkChipsContainer, recordedTagChipOptions);
    const cleanup = () => {
        if (recordingHubConnection) {
            void recordingHubConnection.stop();
            recordingHubConnection = null;
        }
    };
    await loadRecordings(currentPage, sortBy, isDescending, searchQuery);
    if (signal.aborted) {
        return;
    }
    await initializeRecordingHubConnectionAsync();
    const onResize = () => {
        const currentMobileView = isMobileView();
        if (currentMobileView !== previousMobileView) {
            previousMobileView = currentMobileView;
            renderRecordings(lastLoadedRecordings);
        }
    };
    window.addEventListener('beforeunload', cleanup);
    window.addEventListener('resize', onResize);
    if (signal.aborted) {
        cleanup();
    }
    return () => {
        window.removeEventListener('beforeunload', cleanup);
        window.removeEventListener('resize', onResize);
        cleanup();
    };
});
/**
 * 録音番組一覧取得
 * @param page
 * @param sortBy
 * @param isDescending
 * @param searchQuery
 */
async function loadRecordings(page, sortBy, isDescending, searchQuery) {
    const signal = activePageSignal;
    // テーブルヘッダーのソート項目のアイコンを調整
    Object.entries(sortingElements).forEach(([elementId, sortKey]) => {
        const element = document.getElementById(elementId);
        element.classList.remove('sort-up', 'sort-down');
        if (sortKey === sortBy) {
            const sortIcon = isDescending ? 'sort-down' : 'sort-up';
            element.classList.add(`${sortIcon}`);
        }
    });
    const queryString = new URLSearchParams({
        page: page.toString(),
        pageSize: currentPageSize.toString(),
        sortBy,
        isDescending: isDescending.toString(),
        searchQuery
    });
    if (withinDays !== null) {
        queryString.set('withinDays', withinDays.toString());
    }
    if (stationIdFilter) {
        queryString.set('stationId', stationIdFilter);
    }
    if (selectedTagIds.length > 0) {
        queryString.set('tagIds', selectedTagIds.join(','));
        queryString.set('tagMode', tagMode);
    }
    if (untaggedOnly) {
        queryString.set('untaggedOnly', 'true');
    }
    if (unlistenedOnly) {
        queryString.set('unlistenedOnly', 'true');
    }
    const response = await fetch(`${API_ENDPOINTS.PROGRAM_RECORDED}?${queryString.toString()}`);
    const result = await response.json();
    if (signal?.aborted) {
        return;
    }
    const data = result.data;
    lastLoadedRecordings = data.recordings;
    renderRecordings(data.recordings);
    renderPagination(data.totalRecords, page, currentPageSize);
}
;
function isMobileView() {
    return window.matchMedia('(max-width: 768px)').matches;
}
/**
 * 録音番組一覧の放送局フィルタ候補を取得
 */
async function loadStationFilters() {
    const stationSelect = document.getElementById('recorded-station-select');
    if (!stationSelect) {
        return;
    }
    try {
        const response = await fetch(API_ENDPOINTS.PROGRAM_RECORDED_STATIONS);
        const result = await response.json();
        const stations = result.data ?? [];
        stations.forEach((station) => {
            const option = document.createElement('option');
            option.value = station.stationId;
            option.textContent = station.stationName;
            stationSelect.appendChild(option);
        });
    }
    catch (error) {
        console.error('放送局フィルタ候補の取得に失敗しました。', error);
    }
}
/**
 * タグ一覧の取得
 */
async function loadTags() {
    const tagFilterSelect = document.getElementById('recorded-tag-filter');
    const tagBulkSelect = document.getElementById('recorded-tag-bulk');
    if (!tagFilterSelect || !tagBulkSelect) {
        return;
    }
    try {
        const response = await fetch(API_ENDPOINTS.TAGS);
        const result = await response.json();
        const tags = result.data ?? [];
        tagFilterSelect.innerHTML = '';
        tagBulkSelect.innerHTML = '';
        tags.forEach((tag) => {
            const filterOption = document.createElement('option');
            filterOption.value = tag.id;
            filterOption.textContent = tag.name;
            tagFilterSelect.appendChild(filterOption);
            const bulkOption = document.createElement('option');
            bulkOption.value = tag.id;
            bulkOption.textContent = tag.name;
            tagBulkSelect.appendChild(bulkOption);
        });
    }
    catch (error) {
        console.error('タグ一覧の取得に失敗しました。', error);
    }
}
/**
 * 録音一覧への一括タグ操作
 */
async function executeBulkTagOperation(endpoint, tagIds, successMessage, verificationToken) {
    if (selectedRecordingIds.size === 0) {
        showGlobalToast("録音が選択されていません。", false);
        return;
    }
    if (tagIds.length === 0) {
        showGlobalToast("タグが選択されていません。", false);
        return;
    }
    const payload = {
        recordingIds: Array.from(selectedRecordingIds),
        tagIds: tagIds
    };
    try {
        const response = await fetch(endpoint, {
            method: 'POST',
            headers: createMutationHeaders(verificationToken),
            body: JSON.stringify(payload)
        });
        const result = await response.json();
        if (!result.success) {
            showGlobalToast(result.message ?? "更新に失敗しました。", false);
            return;
        }
        const data = result.data;
        const message = `${successMessage} 成功:${data.successCount} 件 / スキップ:${data.skipCount} 件 / 失敗:${data.failCount} 件`;
        showGlobalToast(message, data.failCount === 0);
        if (data.failCount === 0) {
            selectedRecordingIds.clear();
            const selectAllCheckbox = document.getElementById('recordings-select-all');
            if (selectAllCheckbox) {
                selectAllCheckbox.checked = false;
            }
        }
        await loadRecordings(currentPage, sortBy, isDescending, searchQuery);
    }
    catch (error) {
        console.error('タグ一括操作に失敗しました。', error);
        showGlobalToast("タグ操作に失敗しました。", false);
    }
}
/**
 * 録音一覧への一括削除
 */
async function executeBulkDeleteOperation(verificationToken) {
    if (selectedRecordingIds.size === 0) {
        showGlobalToast("録音が選択されていません。", false);
        return;
    }
    const deleteMode = await showBulkDeleteModeDialog(selectedRecordingIds.size);
    if (!deleteMode) {
        return;
    }
    const payload = {
        recordingIds: Array.from(selectedRecordingIds),
        deleteFiles: deleteMode === 'files-and-db'
    };
    try {
        const response = await fetch(API_ENDPOINTS.DELETE_PROGRAM_BULK, {
            method: 'POST',
            headers: createMutationHeaders(verificationToken),
            body: JSON.stringify(payload)
        });
        const result = await response.json();
        if (!response.ok || !result.success) {
            showGlobalToast(result.message ?? "一括削除に失敗しました。", false);
            return;
        }
        const data = result.data;
        if (!data || data.failCount === 0) {
            selectedRecordingIds.clear();
            const selectAllCheckbox = document.getElementById('recordings-select-all');
            if (selectAllCheckbox) {
                selectAllCheckbox.checked = false;
            }
        }
        else {
            const failedSet = new Set((data.failedRecordingIds ?? []).map(id => id.toString()));
            Array.from(selectedRecordingIds).forEach((id) => {
                if (!failedSet.has(id)) {
                    selectedRecordingIds.delete(id);
                }
            });
        }
        if (data) {
            const skipCount = data.skipCount ?? 0;
            const successMessage = data.failCount === 0 && skipCount === 0
                ? `${data.successCount}件を削除しました。`
                : `${data.successCount}件削除、${skipCount}件スキップ、${data.failCount}件失敗しました。`;
            showGlobalToast(successMessage, data.failCount === 0);
        }
        else {
            showGlobalToast("一括削除を実行しました。", true);
        }
        await loadRecordings(currentPage, sortBy, isDescending, searchQuery);
    }
    catch (error) {
        console.error('一括削除に失敗しました。', error);
        showGlobalToast("一括削除に失敗しました。", false);
    }
}
/**
 * 録音一覧への一括既読/未読操作
 */
async function executeBulkListenedOperation(isListened, verificationToken) {
    if (selectedRecordingIds.size === 0) {
        showGlobalToast("録音が選択されていません。", false);
        return;
    }
    const payload = {
        recordingIds: Array.from(selectedRecordingIds),
        isListened
    };
    try {
        const response = await fetch(API_ENDPOINTS.RECORDING_LISTENED_BULK_UPDATE, {
            method: 'POST',
            headers: createMutationHeaders(verificationToken),
            body: JSON.stringify(payload)
        });
        const result = await response.json();
        if (!response.ok || !result.success) {
            showGlobalToast(result.message ?? "一括更新に失敗しました。", false);
            return;
        }
        const data = result.data;
        if (data && data.failCount === 0) {
            selectedRecordingIds.clear();
            const selectAllCheckbox = document.getElementById('recordings-select-all');
            if (selectAllCheckbox) {
                selectAllCheckbox.checked = false;
            }
        }
        if (data) {
            const actionText = isListened ? "視聴済みに更新" : "未視聴に更新";
            const message = `${actionText}しました。 成功:${data.successCount}件 / スキップ:${data.skipCount}件 / 失敗:${data.failCount}件`;
            showGlobalToast(message, data.failCount === 0);
        }
        else {
            showGlobalToast(isListened ? "一括で視聴済みに更新しました。" : "一括で未視聴に更新しました。", true);
        }
        await loadRecordings(currentPage, sortBy, isDescending, searchQuery);
    }
    catch (error) {
        console.error('一括既読/未読更新に失敗しました。', error);
        showGlobalToast("一括更新に失敗しました。", false);
    }
}
/**
 * 一括削除方式選択モーダルを表示する
 */
function showBulkDeleteModeDialog(targetCount) {
    return new Promise((resolve) => {
        const existing = document.getElementById('recording-bulk-delete-modal');
        if (existing) {
            existing.remove();
        }
        const modal = document.createElement('div');
        modal.className = 'modal is-active';
        modal.id = 'recording-bulk-delete-modal';
        modal.innerHTML = `
            <div class="modal-background"></div>
            <div class="modal-card" style="max-width: 34rem;">
                <header class="modal-card-head">
                    <p class="modal-card-title">一括削除の確認</p>
                    <button class="delete" aria-label="close"></button>
                </header>
                <section class="modal-card-body">
                    <p class="mb-4">${targetCount}件の録音を削除します。削除方法を選択してください。</p>
                    <div class="buttons is-flex is-flex-direction-column">
                        <button type="button" class="button is-danger is-fullwidth" data-action="files-and-db">ファイルごと削除</button>
                        <button type="button" class="button is-info is-fullwidth" data-action="db-only">一覧からのみ削除</button>
                    </div>
                </section>
                <footer class="modal-card-foot">
                    <button type="button" class="button is-light" data-action="cancel">キャンセル</button>
                </footer>
            </div>
        `;
        const close = (value) => {
            modal.remove();
            resolve(value);
        };
        const background = modal.querySelector('.modal-background');
        const closeButton = modal.querySelector('.delete');
        const cancelButton = modal.querySelector('[data-action="cancel"]');
        const filesAndDbButton = modal.querySelector('[data-action="files-and-db"]');
        const dbOnlyButton = modal.querySelector('[data-action="db-only"]');
        background?.addEventListener('click', () => close(null));
        closeButton?.addEventListener('click', () => close(null));
        cancelButton?.addEventListener('click', () => close(null));
        filesAndDbButton?.addEventListener('click', async () => {
            const confirmed = await showConfirmDialog('録音ファイルを削除すると元に戻せません。\n本当に実行しますか？', { title: '最終確認', okText: '実行する', cancelText: 'キャンセル' });
            if (!confirmed) {
                return;
            }
            close('files-and-db');
        });
        dbOnlyButton?.addEventListener('click', () => close('db-only'));
        document.body.appendChild(modal);
    });
}
/**
 * 録音番組一覧出力
 * @param recordings
 */
function renderRecordings(recordings) {
    const tableBody = document.getElementById('recordings-table-body');
    const mobileList = document.getElementById('recordings-mobile-list');
    tableBody.innerHTML = '';
    mobileList.innerHTML = '';
    const tableTemplate = document.getElementById('table-template');
    const mobileTemplate = document.getElementById('recordings-mobile-template');
    const mobile = isMobileView();
    recordings.forEach((recording) => {
        const row = mobile
            ? mobileTemplate.content.cloneNode(true)
            : tableTemplate.content.cloneNode(true);
        const rowElement = row.querySelector('.recording-row');
        if (rowElement) {
            rowElement.id = `row-${recording.id}`;
            rowElement.dataset.recordingId = recording.id;
        }
        const tags = recording.tags ?? [];
        setTextContent(row, ".program-title-text", `[${recording.stationName}]${recording.title}`);
        const listenedStateMarker = row.querySelector(".listened-state-marker");
        const markAsListenedInRow = () => {
            const target = listenedStateMarker;
            if (!target) {
                return;
            }
            target.textContent = "";
            target.classList.remove("is-visible");
            target.setAttribute("aria-hidden", "true");
            target.style.display = "none";
            if (!recording.isListened) {
                recording.isListened = true;
            }
            lastLoadedRecordings = lastLoadedRecordings.map((x) => x.id === recording.id ? { ...x, isListened: true } : x);
        };
        if (listenedStateMarker) {
            if (recording.isListened) {
                listenedStateMarker.textContent = "";
                listenedStateMarker.classList.remove("is-visible");
                listenedStateMarker.setAttribute("aria-hidden", "true");
                listenedStateMarker.style.display = "none";
            }
            else {
                listenedStateMarker.textContent = "未視聴";
                listenedStateMarker.classList.add("is-visible");
                listenedStateMarker.setAttribute("aria-hidden", "false");
                listenedStateMarker.style.display = "";
            }
        }
        setTextContent(row, ".startDateTime", formatDisplayDateTime(new Date(recording.startDateTime)));
        setTextContent(row, ".duration", formatDuration(recording.duration));
        if (tags.length === 0) {
            const tagButton = row.querySelector(".tag-list-button");
            if (tagButton) {
                tagButton.classList.add("is-disabled-placeholder");
                tagButton.disabled = true;
                tagButton.title = "タグなし";
                tagButton.setAttribute("aria-label", "タグなし");
                tagButton.tabIndex = -1;
            }
        }
        else {
            setEventListener(row, ".tag-list-button", "click", () => openTagsModal(recording.title, tags));
        }
        const selectElm = row.querySelector('input.recording-select');
        if (selectElm) {
            selectElm.dataset.recordingId = recording.id;
            selectElm.checked = selectedRecordingIds.has(recording.id);
            selectElm.addEventListener('change', () => {
                if (selectElm.checked) {
                    selectedRecordingIds.add(recording.id);
                }
                else {
                    selectedRecordingIds.delete(recording.id);
                }
            });
        }
        const downloadButtonElm = row.querySelector(".download-button");
        downloadButtonElm.href = `${API_ENDPOINTS.DOWNLOAD_PROGRAM}${recording.id}`;
        downloadButtonElm.addEventListener("click", () => {
            markAsListenedInRow();
        });
        const playerButtonElm = row.querySelector(".player-button");
        if (playerButtonElm) {
            playerButtonElm.dataset.recordingId = recording.id;
            setRecordedPlaybackButtonState(playerButtonElm, isCurrentRecordingPlaying(recording.id));
        }
        playerButtonElm?.addEventListener("click", (event) => {
            event.preventDefault();
            if (isCurrentRecordingPlaying(recording.id)) {
                stopCurrentPlayback();
                return;
            }
            markAsListenedInRow();
            void playProgram(recording.id);
        });
        const deleteButtonElm = row.querySelector(".delete-button");
        deleteButtonElm?.addEventListener("click", (event) => {
            event.preventDefault();
            deleteProgram(recording.id);
        });
        if (mobile) {
            mobileList.appendChild(row);
        }
        else {
            tableBody.appendChild(row);
        }
    });
    const selectAllCheckbox = document.getElementById('recordings-select-all');
    if (selectAllCheckbox) {
        const rowCheckboxes = document.querySelectorAll('input.recording-select');
        const checkedCount = Array.from(rowCheckboxes).filter(input => input.checked).length;
        selectAllCheckbox.checked = rowCheckboxes.length > 0 && checkedCount === rowCheckboxes.length;
    }
    syncRecordedListPlaybackButtons();
}
;
function renderPagination(totalRecords, currentPage, pageSize) {
    const paginationList = document.getElementById('pagination-list');
    paginationList.innerHTML = '';
    // 最大ページ数
    const totalPages = Math.ceil(totalRecords / pageSize);
    // 1ページで収まるならページネーションは非表示
    if (totalPages <= 1)
        return;
    const maxPagesToShow = 11;
    let startPage = Math.max(1, currentPage - Math.floor(maxPagesToShow / 2));
    let endPage = Math.min(totalPages, startPage + maxPagesToShow - 1);
    if (endPage - startPage < maxPagesToShow - 1) {
        startPage = Math.max(1, endPage - maxPagesToShow + 1);
    }
    const prevPageButton = document.createElement('a');
    prevPageButton.className = 'pagination-previous';
    prevPageButton.textContent = '<';
    prevPageButton.onclick = () => {
        if (currentPage > 1) {
            currentPage--;
            loadRecordings(currentPage, sortBy, isDescending, searchQuery);
        }
    };
    const prevPageItem = document.createElement('li');
    prevPageItem.appendChild(prevPageButton);
    paginationList.appendChild(prevPageItem);
    if (startPage > 1) {
        const firstPageItem = document.createElement('li');
        const firstPageLink = document.createElement('a');
        firstPageLink.className = 'pagination-link';
        firstPageLink.textContent = '1';
        firstPageLink.addEventListener('click', () => {
            currentPage = 1;
            loadRecordings(currentPage, sortBy, isDescending, searchQuery);
        });
        firstPageItem.appendChild(firstPageLink);
        paginationList.appendChild(firstPageItem);
        if (startPage > 2) {
            const ellipsisItem = document.createElement('li');
            const ellipsis = document.createElement('span');
            ellipsis.className = 'pagination-ellipsis';
            ellipsis.textContent = '...';
            ellipsisItem.appendChild(ellipsis);
            paginationList.appendChild(ellipsisItem);
        }
    }
    for (let i = startPage; i <= endPage; i++) {
        const listItem = document.createElement('li');
        const link = document.createElement('a');
        link.className = 'pagination-link';
        if (i === currentPage) {
            link.classList.add('is-current');
        }
        link.textContent = i.toString();
        link.addEventListener('click', () => {
            currentPage = i;
            loadRecordings(currentPage, sortBy, isDescending, searchQuery);
        });
        listItem.appendChild(link);
        paginationList.appendChild(listItem);
    }
    if (endPage < totalPages) {
        if (endPage < totalPages - 1) {
            const ellipsisItem = document.createElement('li');
            const ellipsis = document.createElement('span');
            ellipsis.className = 'pagination-ellipsis';
            ellipsis.textContent = '...';
            ellipsisItem.appendChild(ellipsis);
            paginationList.appendChild(ellipsisItem);
        }
        const lastPageItem = document.createElement('li');
        const lastPageLink = document.createElement('a');
        lastPageLink.className = 'pagination-link';
        lastPageLink.textContent = totalPages.toString();
        lastPageLink.addEventListener('click', () => {
            currentPage = totalPages;
            loadRecordings(currentPage, sortBy, isDescending, searchQuery);
        });
        lastPageItem.appendChild(lastPageLink);
        paginationList.appendChild(lastPageItem);
    }
    const nextPageButton = document.createElement('a');
    nextPageButton.className = 'pagination-next';
    nextPageButton.classList.toggle('is-disabled', currentPage === totalPages);
    nextPageButton.textContent = '>';
    nextPageButton.onclick = () => {
        if (currentPage < totalPages) {
            currentPage++;
            loadRecordings(currentPage, sortBy, isDescending, searchQuery);
        }
    };
    const nextPageItem = document.createElement('li');
    nextPageItem.appendChild(nextPageButton);
    paginationList.appendChild(nextPageItem);
}
;
/**
 * 録音時間生成
 * @param seconds
 * @returns
 */
function formatDuration(seconds) {
    const roundedSeconds = Math.max(0, Math.round(seconds));
    const hours = Math.floor(roundedSeconds / 3600);
    const minutes = Math.floor((roundedSeconds % 3600) / 60);
    const remainingSeconds = roundedSeconds % 60;
    const formattedHours = String(hours).padStart(2, '0');
    const formattedMinutes = String(minutes).padStart(2, '0');
    const formattedSeconds = String(remainingSeconds).padStart(2, '0');
    return `${formattedHours}:${formattedMinutes}:${formattedSeconds}`;
}
;
/**
 * 録音タグ一覧モーダルを開く
 */
function openTagsModal(programTitle, tags) {
    const modalElement = tagsModalElement;
    const modalTitleElement = tagsModalTitleElement;
    const modalListElement = tagsModalListElement;
    if (!modalElement || !modalTitleElement || !modalListElement) {
        return;
    }
    modalTitleElement.textContent = programTitle;
    modalListElement.innerHTML = '';
    if (tags.length === 0) {
        const emptyItem = document.createElement('li');
        emptyItem.textContent = 'タグなし';
        modalListElement.appendChild(emptyItem);
    }
    else {
        tags.forEach((tag) => {
            const item = document.createElement('li');
            item.textContent = tag;
            modalListElement.appendChild(item);
        });
    }
    modalElement.classList.add('is-active');
}
function closeTagsModal() {
    if (!tagsModalElement) {
        return;
    }
    tagsModalElement.classList.remove('is-active');
}
/**
 * 指定録音IDの未視聴表示を即時反映で解除
 */
function markRecordingAsListenedInUi(recordId) {
    lastLoadedRecordings = lastLoadedRecordings.map((recording) => {
        if (recording.id !== recordId || recording.isListened) {
            return recording;
        }
        return { ...recording, isListened: true };
    });
    const markers = document.querySelectorAll(`.recording-row[data-recording-id="${recordId}"] .listened-state-marker`);
    markers.forEach((marker) => {
        marker.textContent = "";
        marker.classList.remove("is-visible");
        marker.setAttribute("aria-hidden", "true");
    });
}
/**
 * 指定録音IDを一覧UIから即時除去
 */
function removeRecordingFromUi(recordId) {
    if (currentPlayingRecordingId === recordId) {
        stopCurrentPlayback();
    }
    selectedRecordingIds.delete(recordId);
    lastLoadedRecordings = lastLoadedRecordings.filter((recording) => recording.id !== recordId);
    renderRecordings(lastLoadedRecordings);
}
/**
 * 録画番組再生処理
 * @param programId
 * @param serviceKind
 */
async function playProgram(recordId) {
    const sourceUrl = `/api/recordings/play/${recordId}`;
    const title = lastLoadedRecordings.find((recording) => recording.id === recordId)?.title ?? null;
    await playPlayerSource({ sourceUrl, recordId, title, kind: 'recording' });
}
function createPlayerJumpControls(audioElm) {
    const continuousButton = document.createElement('button');
    continuousButton.type = 'button';
    continuousButton.className = 'player-jump-toggle player-icon-button';
    continuousButton.innerHTML = `
        <svg class="player-next-sequence-icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
            <path class="list-line" d="M3 6h13" />
            <path class="list-line" d="M3 11h10" />
            <path class="list-line" d="M3 16h13" />
            <path class="play-triangle" d="M12.3 7.8l6.2 4.2-6.2 4.2z" />
        </svg>
    `;
    const updateContinuousButtonState = () => {
        const enabled = isContinuousPlaybackEnabled();
        continuousButton.classList.toggle('is-enabled', enabled);
        continuousButton.setAttribute('aria-pressed', enabled ? 'true' : 'false');
        continuousButton.setAttribute('aria-label', enabled ? '連続再生 ON' : '連続再生 OFF');
        continuousButton.title = enabled ? '連続再生 ON' : '連続再生 OFF';
    };
    continuousButton.addEventListener('click', () => {
        localStorage.setItem(continuousPlaybackStorageKey, (!isContinuousPlaybackEnabled()).toString());
        updateContinuousButtonState();
    });
    return createStandardPlayerJumpControls(audioElm, {
        playbackRateOptions: playerPlaybackRateOptions,
        onUpdateLabels: updateContinuousButtonState,
        createSideButtons: () => [continuousButton]
    });
}
function isContinuousPlaybackEnabled() {
    return localStorage.getItem(continuousPlaybackStorageKey) === 'true';
}
function getNextRecordingId(currentId) {
    const currentIndex = lastLoadedRecordings.findIndex((recording) => recording.id === currentId);
    if (currentIndex < 0) {
        return null;
    }
    const next = lastLoadedRecordings[currentIndex + 1];
    return next?.id ?? null;
}
async function handlePlaybackEnded() {
    const finishedId = currentPlayingRecordingId;
    if (!isContinuousPlaybackEnabled() || !finishedId) {
        finishPlayer();
        return;
    }
    const nextRecordingId = getNextRecordingId(finishedId);
    if (!nextRecordingId) {
        finishPlayer();
        return;
    }
    markRecordingAsListenedInUi(finishedId);
    await playProgram(nextRecordingId);
}
/**
* 録画番組削除処理
* @param programId
* @param serviceKind
*/
async function deleteProgram(recordId) {
    const verificationToken = document.getElementById('VerificationToken')?.value ?? '';
    const deleteMode = await showBulkDeleteModeDialog(1);
    if (!deleteMode) {
        return;
    }
    const deleteFiles = deleteMode === 'files-and-db';
    try {
        const endpoint = `/api/recordings/${encodeURIComponent(recordId)}?deleteFiles=${deleteFiles.toString()}`;
        const response = await fetch(endpoint, {
            method: 'DELETE',
            headers: createMutationHeaders(verificationToken)
        });
        const result = await response.json();
        if (result.success) {
            if (!result.data) {
                showGlobalToast(result.message ?? "削除に失敗しました。", false);
                return;
            }
            removeRecordingFromUi(recordId);
            showGlobalToast(result.message ?? "削除しました。");
            await loadRecordings(currentPage, sortBy, isDescending, searchQuery);
            return;
        }
        showGlobalToast(result.message ?? "削除に失敗しました。", false);
    }
    catch (error) {
        console.error('Error:', error);
        showGlobalToast('エラーが発生しました。', false);
    }
}
function createMutationHeaders(verificationToken) {
    const headers = {
        'Content-Type': 'application/json'
    };
    if (verificationToken) {
        headers['RequestVerificationToken'] = verificationToken;
    }
    return headers;
}
//# sourceMappingURL=recorded.js.map