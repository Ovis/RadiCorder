import { API_ENDPOINTS } from './const.js';
import { showConfirmDialog } from './feedback.js';
import { initExternalImport } from './setting-external-import.js';
import { initSettingMaintenance } from './setting-maintenance.js';
document.addEventListener('DOMContentLoaded', async () => {
    const verificationToken = document.getElementById('VerificationToken').value;
    const resultToast = document.getElementById('result-toast');
    const resultToastMessage = document.getElementById('result-toast-message');
    const resultToastClose = document.getElementById('result-toast-close');
    const recordDirectoryPathUpdateButton = document.getElementById('update-record-directory-path-btn');
    const recordFileNameTemplateUpdateButton = document.getElementById('UpdateRecordFileNameTemplateBtn');
    const recordDurationUpdateButton = document.getElementById('update-record-duratioh-btn');
    const recordDurationUpdateButtonDesktop = document.getElementById('update-record-duratioh-btn-desktop');
    const noticeUpdateButton = document.getElementById('update-notice-setting-btn');
    const unreadBadgeNoticeCategoriesUpdateButton = document.getElementById('update-unread-badge-notice-categories-btn');
    const updateRadiruAreaBtn = document.getElementById('UpdateRadiruAreaBtn');
    const updateExternalServiceUserAgentBtn = document.getElementById('UpdateExternalServiceUserAgentBtn');
    const updateRadiruRequestSettingsBtn = document.getElementById('update-radiru-request-settings-btn');
    const updateRadiruRequestSettingsBtnDesktop = document.getElementById('update-radiru-request-settings-btn-desktop');
    const programUpdateButton = document.getElementById('update-program-btn');
    const programUpdateStatusText = document.getElementById('program-update-status-text');
    const programUpdateLastCompletedText = document.getElementById('program-update-last-completed-text');
    const radikoLoginUpdateButton = document.getElementById('update-radiko-login-btn');
    const radikoLoginClearButton = document.getElementById('clear-radiko-login-btn');
    const refreshRadikoAreaButton = document.getElementById('refresh-radiko-area-btn');
    const externalImportTimeZoneSaveButton = document.getElementById('external-import-timezone-save-btn');
    const storageLowSpaceThresholdUpdateButton = document.getElementById('update-storage-low-space-threshold-btn');
    const updateMonitoringAdvancedBtn = document.getElementById('update-monitoring-advanced-btn');
    const updateMonitoringAdvancedBtnDesktop = document.getElementById('update-monitoring-advanced-btn-desktop');
    const updateClockSkewMonitoringBtn = document.getElementById('update-clock-skew-monitoring-btn');
    const updateClockSkewMonitoringBtnDesktop = document.getElementById('update-clock-skew-monitoring-btn-desktop');
    const mergeTagsFromMatchedRulesUpdateButton = document.getElementById('update-merge-tags-from-matched-rules-btn');
    const embedProgramImageOnRecordUpdateButton = document.getElementById('update-embed-program-image-on-record-btn');
    const resumePlaybackAcrossPagesUpdateButton = document.getElementById('update-resume-playback-across-pages-btn');
    const releaseCheckIntervalUpdateButton = document.getElementById('update-release-check-interval-btn');
    const duplicateDetectionIntervalUpdateButton = document.getElementById('update-duplicate-detection-interval-btn');
    const duplicateDetectionIntervalUpdateButtonDesktop = document.getElementById('update-duplicate-detection-interval-btn-desktop');
    const generalTabButton = document.getElementById('setting-tab-general');
    const advancedTabButton = document.getElementById('setting-tab-advanced');
    const tagsTabButton = document.getElementById('setting-tab-tags');
    const externalImportTabButton = document.getElementById('setting-tab-external-import');
    const maintenanceTabButton = document.getElementById('setting-tab-maintenance');
    const generalPanel = document.getElementById('setting-panel-general');
    const advancedPanel = document.getElementById('setting-panel-advanced');
    const tagsPanel = document.getElementById('setting-panel-tags');
    const externalImportPanel = document.getElementById('setting-panel-external-import');
    const maintenancePanel = document.getElementById('setting-panel-maintenance');
    const tagCreateInput = document.getElementById('tag-create-name-setting');
    const tagCreateButton = document.getElementById('tag-create-button-setting');
    const tagTableBody = document.getElementById('tag-table-body-setting');
    const tagRowTemplate = document.getElementById('tag-table-row-template-setting');
    const tagManageModal = document.getElementById('tag-manage-modal');
    const tagModalCloseButton = document.getElementById('tag-modal-close');
    const tagModalCurrentName = document.getElementById('tag-modal-current-name');
    const tagModalRenameInput = document.getElementById('tag-modal-rename-input');
    const tagModalRenameButton = document.getElementById('tag-modal-rename-button');
    const tagModalMergeSelect = document.getElementById('tag-modal-merge-select');
    const tagModalMergeButton = document.getElementById('tag-modal-merge-button');
    let tagsLoaded = false;
    let settingTags = [];
    let selectedTagId = null;
    let programUpdateHubConnection = null;
    let toastTimerId;
    const showToast = (message, isSuccess = true) => {
        if (!resultToast || !resultToastMessage) {
            return;
        }
        resultToastMessage.textContent = message;
        resultToast.classList.toggle('is-error', !isSuccess);
        resultToast.classList.add('is-active');
        if (toastTimerId !== undefined) {
            window.clearTimeout(toastTimerId);
        }
        toastTimerId = window.setTimeout(() => {
            resultToast.classList.remove('is-active');
        }, 2500);
    };
    const switchSettingTab = async (tab) => {
        if (!generalPanel || !advancedPanel || !tagsPanel || !externalImportPanel || !maintenancePanel || !generalTabButton || !advancedTabButton || !tagsTabButton || !externalImportTabButton || !maintenanceTabButton) {
            return;
        }
        const isGeneral = tab === 'general';
        const isAdvanced = tab === 'advanced';
        const isTags = tab === 'tags';
        const isExternalImport = tab === 'external-import';
        const isMaintenance = tab === 'maintenance';
        generalPanel.classList.toggle('hidden', !isGeneral);
        advancedPanel.classList.toggle('hidden', !isAdvanced);
        tagsPanel.classList.toggle('hidden', !isTags);
        externalImportPanel.classList.toggle('hidden', !isExternalImport);
        maintenancePanel.classList.toggle('hidden', !isMaintenance);
        const activeClass = ['border-slate-300', 'bg-white', 'text-slate-900', 'font-semibold', 'shadow-sm', 'ring-1', 'ring-slate-200'];
        const inactiveClass = ['border-slate-300', 'bg-slate-50', 'text-slate-700', 'font-semibold', 'shadow-none'];
        [generalTabButton, advancedTabButton, tagsTabButton, externalImportTabButton, maintenanceTabButton].forEach((button) => {
            button.classList.remove(...activeClass, ...inactiveClass);
        });
        if (isGeneral) {
            generalTabButton.classList.add(...activeClass);
            advancedTabButton.classList.add(...inactiveClass);
            tagsTabButton.classList.add(...inactiveClass);
            externalImportTabButton.classList.add(...inactiveClass);
            maintenanceTabButton.classList.add(...inactiveClass);
        }
        else if (isAdvanced) {
            generalTabButton.classList.add(...inactiveClass);
            advancedTabButton.classList.add(...activeClass);
            tagsTabButton.classList.add(...inactiveClass);
            externalImportTabButton.classList.add(...inactiveClass);
            maintenanceTabButton.classList.add(...inactiveClass);
        }
        else if (isTags) {
            generalTabButton.classList.add(...inactiveClass);
            advancedTabButton.classList.add(...inactiveClass);
            tagsTabButton.classList.add(...activeClass);
            externalImportTabButton.classList.add(...inactiveClass);
            maintenanceTabButton.classList.add(...inactiveClass);
            if (!tagsLoaded) {
                await loadTagsForSetting();
                tagsLoaded = true;
            }
        }
        else if (isExternalImport) {
            generalTabButton.classList.add(...inactiveClass);
            advancedTabButton.classList.add(...inactiveClass);
            tagsTabButton.classList.add(...inactiveClass);
            externalImportTabButton.classList.add(...activeClass);
            maintenanceTabButton.classList.add(...inactiveClass);
        }
        else {
            generalTabButton.classList.add(...inactiveClass);
            advancedTabButton.classList.add(...inactiveClass);
            tagsTabButton.classList.add(...inactiveClass);
            externalImportTabButton.classList.add(...inactiveClass);
            maintenanceTabButton.classList.add(...activeClass);
        }
    };
    /**
     * 番組表更新状態を表示へ反映する。
     */
    const renderProgramUpdateStatus = (status) => {
        if (programUpdateStatusText) {
            programUpdateStatusText.textContent = status.message;
        }
        if (programUpdateLastCompletedText) {
            programUpdateLastCompletedText.textContent = status.lastCompletedAtUtc
                ? new Date(status.lastCompletedAtUtc).toLocaleString('ja-JP')
                : '-';
        }
    };
    /**
     * 番組表更新状態を取得して表示する。
     */
    const loadProgramUpdateStatusAsync = async () => {
        try {
            const response = await fetch(API_ENDPOINTS.SETTING_PROGRAM_UPDATE_STATUS, { method: 'GET' });
            const result = await response.json();
            if (result.data) {
                renderProgramUpdateStatus(result.data);
            }
        }
        catch (error) {
            console.error('番組表更新状態の取得に失敗しました。', error);
        }
    };
    /**
     * 番組表更新状態のSignalR接続を初期化する。
     */
    const initializeProgramUpdateHubConnectionAsync = async () => {
        const signalRNamespace = window.signalR;
        if (!signalRNamespace) {
            return;
        }
        const connection = new signalRNamespace.HubConnectionBuilder()
            .withUrl('/hubs/program-updates')
            .withAutomaticReconnect()
            .configureLogging(signalRNamespace.LogLevel.Warning)
            .build();
        connection.on('programUpdateStatusChanged', (...args) => {
            const payload = (args[0] ?? null);
            if (!payload) {
                return;
            }
            renderProgramUpdateStatus(payload);
        });
        connection.onreconnected(() => {
            void loadProgramUpdateStatusAsync();
        });
        try {
            await connection.start();
            programUpdateHubConnection = connection;
        }
        catch (error) {
            console.warn('番組表更新SignalR接続の開始に失敗しました。', error);
        }
    };
    const scrollToGeneralSection = (hash, smooth = true) => {
        if (!hash || !hash.startsWith('#settings-')) {
            return;
        }
        const target = document.querySelector(hash);
        if (!target) {
            return;
        }
        const header = document.querySelector('.rk-header');
        const headerHeight = header?.offsetHeight ?? 0;
        const top = Math.max(0, target.getBoundingClientRect().top + window.scrollY - headerHeight - 8);
        window.scrollTo({ top, behavior: smooth ? 'smooth' : 'auto' });
    };
    const setTagRowText = (root, selector, value) => {
        const elm = root.querySelector(selector);
        if (!elm) {
            return;
        }
        elm.textContent = value;
    };
    const closeTagModal = () => {
        if (!tagManageModal) {
            return;
        }
        tagManageModal.classList.remove('is-active');
        selectedTagId = null;
    };
    const openTagModal = (tagId) => {
        if (!tagManageModal || !tagModalCurrentName || !tagModalRenameInput || !tagModalMergeSelect) {
            return;
        }
        const current = settingTags.find(tag => tag.id === tagId);
        if (!current) {
            return;
        }
        selectedTagId = tagId;
        tagModalCurrentName.textContent = current.name;
        tagModalRenameInput.value = current.name;
        tagModalMergeSelect.innerHTML = '';
        const mergeCandidates = settingTags.filter(tag => tag.id !== tagId);
        if (mergeCandidates.length === 0) {
            const option = document.createElement('option');
            option.value = '';
            option.textContent = '統合先の候補がありません';
            tagModalMergeSelect.appendChild(option);
            tagModalMergeSelect.disabled = true;
            if (tagModalMergeButton) {
                tagModalMergeButton.disabled = true;
            }
        }
        else {
            mergeCandidates.forEach(tag => {
                const option = document.createElement('option');
                option.value = tag.id;
                option.textContent = tag.name;
                tagModalMergeSelect.appendChild(option);
            });
            tagModalMergeSelect.disabled = false;
            if (tagModalMergeButton) {
                tagModalMergeButton.disabled = false;
            }
        }
        tagManageModal.classList.add('is-active');
    };
    const loadTagsForSetting = async () => {
        if (!tagTableBody || !tagRowTemplate) {
            return;
        }
        try {
            const response = await fetch(API_ENDPOINTS.TAGS);
            const result = await response.json();
            const tags = result.data ?? [];
            settingTags = tags;
            tagTableBody.innerHTML = '';
            tags.forEach((tag) => {
                const row = tagRowTemplate.content.cloneNode(true);
                const tr = row.querySelector('tr');
                tr.dataset.tagId = tag.id;
                setTagRowText(row, '.tag-name', tag.name);
                setTagRowText(row, '.tag-count', tag.recordingCount.toString());
                setTagRowText(row, '.tag-created', new Date(tag.createdAt).toLocaleDateString());
                row.querySelector('.tag-action-button')?.addEventListener('click', () => {
                    openTagModal(tag.id);
                });
                row.querySelector('.tag-delete-inline-button')?.addEventListener('click', async () => {
                    const confirmed = await showConfirmDialog(`タグ「${tag.name}」を削除しますか？`, { okText: '削除する' });
                    if (!confirmed) {
                        return;
                    }
                    try {
                        const response = await fetch(`${API_ENDPOINTS.TAGS}/${tag.id}`, { method: 'DELETE' });
                        const deleteResult = await response.json();
                        if (!deleteResult.success) {
                            showToast(deleteResult.message ?? '削除に失敗しました。', false);
                            return;
                        }
                        showToast(deleteResult.message ?? '削除しました。');
                        await loadTagsForSetting();
                    }
                    catch (error) {
                        const message = error instanceof Error ? error.message : `${error}`;
                        showToast(message, false);
                    }
                });
                tagTableBody.appendChild(row);
            });
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    };
    if (resultToastClose) {
        resultToastClose.addEventListener('click', () => {
            if (resultToast) {
                resultToast.classList.remove('is-active');
            }
        });
    }
    generalTabButton?.addEventListener('click', async () => {
        await switchSettingTab('general');
    });
    advancedTabButton?.addEventListener('click', async () => {
        await switchSettingTab('advanced');
    });
    tagsTabButton?.addEventListener('click', async () => {
        await switchSettingTab('tags');
    });
    externalImportTabButton?.addEventListener('click', async () => {
        await switchSettingTab('external-import');
    });
    maintenanceTabButton?.addEventListener('click', async () => {
        await switchSettingTab('maintenance');
    });
    await switchSettingTab('general');
    const generalSectionLinks = document.querySelectorAll('#setting-panel-general a[href^="#settings-"]');
    generalSectionLinks.forEach((link) => {
        link.addEventListener('click', async (event) => {
            const anchor = event.currentTarget;
            const hash = anchor?.getAttribute('href') ?? '';
            if (!hash) {
                return;
            }
            event.preventDefault();
            await switchSettingTab('general');
            scrollToGeneralSection(hash, true);
            history.replaceState(null, '', hash);
        });
    });
    if (window.location.hash.startsWith('#settings-')) {
        await switchSettingTab('general');
        window.setTimeout(() => {
            scrollToGeneralSection(window.location.hash, false);
        }, 0);
    }
    initExternalImport(verificationToken, showToast);
    initSettingMaintenance(verificationToken, showToast);
    await loadProgramUpdateStatusAsync();
    await initializeProgramUpdateHubConnectionAsync();
    tagCreateButton?.addEventListener('click', async () => {
        const name = tagCreateInput?.value.trim() ?? '';
        if (!name) {
            showToast('タグ名を入力してください。', false);
            return;
        }
        try {
            const requestBody = { name };
            const response = await fetch(API_ENDPOINTS.TAGS, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(requestBody)
            });
            const createResult = await response.json();
            if (!createResult.success) {
                showToast(createResult.message ?? '作成に失敗しました。', false);
                return;
            }
            if (tagCreateInput) {
                tagCreateInput.value = '';
            }
            showToast(createResult.message ?? '作成しました。');
            await loadTagsForSetting();
            tagsLoaded = true;
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    tagModalCloseButton?.addEventListener('click', () => {
        closeTagModal();
    });
    tagManageModal?.querySelector('.modal-background')?.addEventListener('click', () => {
        closeTagModal();
    });
    tagModalRenameButton?.addEventListener('click', async () => {
        if (!selectedTagId || !tagModalRenameInput) {
            return;
        }
        const name = tagModalRenameInput.value.trim();
        if (!name) {
            showToast('新しいタグ名を入力してください。', false);
            return;
        }
        try {
            const requestBody = { name };
            const response = await fetch(`${API_ENDPOINTS.TAGS}/${selectedTagId}`, {
                method: 'PATCH',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(requestBody)
            });
            const patchResult = await response.json();
            if (!patchResult.success) {
                showToast(patchResult.message ?? '更新に失敗しました。', false);
                return;
            }
            showToast(patchResult.message ?? '更新しました。');
            await loadTagsForSetting();
            closeTagModal();
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    tagModalMergeButton?.addEventListener('click', async () => {
        if (!selectedTagId || !tagModalMergeSelect || tagModalMergeSelect.disabled) {
            return;
        }
        const toTagId = tagModalMergeSelect.value;
        if (!toTagId) {
            showToast('統合先タグを選択してください。', false);
            return;
        }
        try {
            const requestBody = { fromTagId: selectedTagId, toTagId };
            const response = await fetch(`${API_ENDPOINTS.TAGS}/merge`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(requestBody)
            });
            const mergeResult = await response.json();
            if (!mergeResult.success) {
                showToast(mergeResult.message ?? '統合に失敗しました。', false);
                return;
            }
            showToast(mergeResult.message ?? '統合しました。');
            await loadTagsForSetting();
            closeTagModal();
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    const parseErrorMessage = async (response) => {
        const fallbackMessage = `リクエストに失敗しました。（HTTP ${response.status}）`;
        try {
            const contentType = response.headers.get('content-type') ?? '';
            if (contentType.includes('application/json')) {
                const body = await response.json();
                if (body?.message && typeof body.message === 'string') {
                    return body.message;
                }
            }
            else {
                const text = (await response.text()).trim();
                if (text.length > 0) {
                    return text;
                }
            }
        }
        catch (error) {
            console.error('Failed to parse error response:', error);
        }
        return fallbackMessage;
    };
    // 更新APIの共通POST。呼び出し側で必要なdata型を明示して受け取る。
    const postData = async (url, data) => {
        try {
            const response = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': verificationToken
                },
                body: JSON.stringify(data)
            });
            if (!response.ok) {
                throw new Error(await parseErrorMessage(response));
            }
            const result = await response.json();
            if (!result.success) {
                throw new Error(result.message ?? '更新に失敗しました。');
            }
            return result;
        }
        catch (error) {
            console.error('There has been a problem with your fetch operation:', error);
            throw error;
        }
    };
    const getPreferredElementByIds = (ids) => {
        let fallback = null;
        for (const id of ids) {
            const element = document.getElementById(id);
            if (!element) {
                continue;
            }
            fallback ??= element;
            if (element.offsetParent !== null) {
                return element;
            }
        }
        return fallback;
    };
    const parseNumberInputByIds = (ids) => {
        const input = getPreferredElementByIds(ids);
        if (input && input.value.trim().length > 0) {
            return Number.parseInt(input.value, 10);
        }
        return Number.NaN;
    };
    recordDirectoryPathUpdateButton.addEventListener('click', async () => {
        const recordDirectoryPathInputElm = document.getElementById('record-directory-path-value');
        const requestBody = {
            directoryPath: recordDirectoryPathInputElm.value
        };
        try {
            await postData(API_ENDPOINTS.SETTING_RECORD_DIC_PATH, requestBody);
            showToast('保存しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    recordFileNameTemplateUpdateButton.addEventListener('click', async () => {
        const recordFileNameTemplateInputElm = document.getElementById('RecordFileNameTemplateValue');
        const requestBody = {
            fileNameTemplate: recordFileNameTemplateInputElm.value
        };
        try {
            await postData(API_ENDPOINTS.SETTING_RECORD_FILENAME_TEMPLATE, requestBody);
            showToast('保存しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    const saveRecordDuration = async () => {
        const recordStartDuration = parseNumberInputByIds([
            'record-start-duration-value',
            'record-start-duration-value-desktop'
        ]);
        const recordEndDuration = parseNumberInputByIds([
            'record-end-duration-value',
            'record-end-duration-value-desktop'
        ]);
        if (isNaN(recordStartDuration) || isNaN(recordEndDuration)) {
            showToast('録音時間のマージンには数値を入力してください。', false);
            return;
        }
        const requestBody = {
            startDuration: recordStartDuration,
            endDuration: recordEndDuration
        };
        try {
            await postData(API_ENDPOINTS.SETTING_DURATION, requestBody);
            showToast('保存しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    };
    recordDurationUpdateButton?.addEventListener('click', saveRecordDuration);
    recordDurationUpdateButtonDesktop?.addEventListener('click', saveRecordDuration);
    updateRadiruAreaBtn.addEventListener('click', async () => {
        const radiruAreaSelect = document.getElementById('RadiruArea');
        const selectedValue = radiruAreaSelect.value;
        const requestBody = {
            radiruArea: selectedValue
        };
        try {
            await postData(API_ENDPOINTS.SETTING_RADIRU_AREA, requestBody);
            showToast('保存しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    updateExternalServiceUserAgentBtn.addEventListener('click', async () => {
        const externalServiceUserAgentInput = document.getElementById('ExternalServiceUserAgent');
        const userAgent = externalServiceUserAgentInput.value.trim();
        if (!userAgent) {
            showToast('User-Agent を入力してください。', false);
            return;
        }
        const requestBody = {
            userAgent
        };
        try {
            await postData(API_ENDPOINTS.SETTING_EXTERNAL_SERVICE_USER_AGENT, requestBody);
            showToast('保存しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    const saveRadiruRequestSettings = async () => {
        const minRequestIntervalMs = parseNumberInputByIds([
            'radiru-api-min-request-interval-ms',
            'radiru-api-min-request-interval-ms-desktop'
        ]);
        const requestJitterMs = parseNumberInputByIds([
            'radiru-api-request-jitter-ms',
            'radiru-api-request-jitter-ms-desktop'
        ]);
        if (Number.isNaN(minRequestIntervalMs) || minRequestIntervalMs < 0 || minRequestIntervalMs > 60000) {
            showToast('最小待機時間は 0〜60000 ミリ秒で入力してください。', false);
            return;
        }
        if (Number.isNaN(requestJitterMs) || requestJitterMs < 0 || requestJitterMs > 60000) {
            showToast('ランダム揺らぎは 0〜60000 ミリ秒で入力してください。', false);
            return;
        }
        try {
            const requestBody = {
                minRequestIntervalMs,
                requestJitterMs
            };
            await postData(API_ENDPOINTS.SETTING_EXTERNAL_SERVICE_RADIRU_REQUEST, requestBody);
            showToast('保存しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    };
    updateRadiruRequestSettingsBtn?.addEventListener('click', saveRadiruRequestSettings);
    updateRadiruRequestSettingsBtnDesktop?.addEventListener('click', saveRadiruRequestSettings);
    noticeUpdateButton.addEventListener('click', async () => {
        const discordWebhookUrlInputElm = document.getElementById('DiscordWebhookUrl');
        const checkboxes = document.querySelectorAll('input[name="SelectedNoticeCategory"]:checked');
        const selectedValues = Array.from(checkboxes).map(checkbox => parseInt(checkbox.value));
        const requestBody = {
            discordWebhookUrl: discordWebhookUrlInputElm.value,
            notificationCategories: selectedValues
        };
        try {
            await postData(API_ENDPOINTS.SETTING_NOTICE, requestBody);
            showToast('保存しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    unreadBadgeNoticeCategoriesUpdateButton?.addEventListener('click', async () => {
        const checkboxes = document.querySelectorAll('input[name="SelectedUnreadBadgeNoticeCategory"]:checked');
        const selectedValues = Array.from(checkboxes).map(checkbox => parseInt(checkbox.value));
        const requestBody = {
            notificationCategories: selectedValues
        };
        try {
            await postData(API_ENDPOINTS.SETTING_UNREAD_BADGE_NOTICE_CATEGORIES, requestBody);
            showToast('保存しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    programUpdateButton.addEventListener('click', async () => {
        try {
            const requestBody = {};
            await postData(API_ENDPOINTS.SETTING_PROGRAM_UPDATE, requestBody);
            showToast('番組表の更新を開始しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : '予期せぬエラーが発生しました。';
            showToast(message, false);
        }
    });
    const ensureRadikoPasswordSavedNote = () => {
        const existing = document.getElementById('RadikoPasswordSavedNote');
        if (existing) {
            return;
        }
        const passwordInput = document.getElementById('RadikoPassword');
        const passwordField = passwordInput?.closest('.field');
        if (!passwordField) {
            return;
        }
        const note = document.createElement('p');
        note.id = 'RadikoPasswordSavedNote';
        note.className = 'text-xs text-zinc-500 mt-2';
        note.textContent = '現在パスワードは保存済みです。変更する場合のみ入力してください。';
        passwordField.appendChild(note);
    };
    radikoLoginUpdateButton.addEventListener('click', async () => {
        const userId = document.getElementById('RadikoUserId').value.trim();
        const password = document.getElementById('RadikoPassword').value.trim();
        if (!userId || !password) {
            showToast('メールアドレスとパスワードを入力してください。', false);
            return;
        }
        const requestBody = {
            userId,
            password
        };
        try {
            await postData(API_ENDPOINTS.SETTING_RADIKO_LOGIN, requestBody);
            ensureRadikoPasswordSavedNote();
            showToast('保存しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    radikoLoginClearButton.addEventListener('click', async () => {
        const confirmed = await showConfirmDialog('radikoログイン情報をログアウトします。\nよろしいですか？', { okText: 'ログアウト' });
        if (!confirmed) {
            return;
        }
        try {
            const requestBody = {};
            await postData(API_ENDPOINTS.SETTING_RADIKO_LOGOUT, requestBody);
            document.getElementById('RadikoUserId').value = '';
            document.getElementById('RadikoPassword').value = '';
            const passwordSavedNote = document.getElementById('RadikoPasswordSavedNote');
            if (passwordSavedNote) {
                passwordSavedNote.remove();
            }
            showToast('radikoログイン情報を削除しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    refreshRadikoAreaButton?.addEventListener('click', async () => {
        try {
            const requestBody = {};
            const result = await postData(API_ENDPOINTS.SETTING_RADIKO_AREA_REFRESH, requestBody);
            showToast(result?.message ?? 'radikoエリア情報を再判定しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    externalImportTimeZoneSaveButton?.addEventListener('click', async () => {
        const timezoneSelect = document.getElementById('external-import-timezone-id');
        const timeZoneId = timezoneSelect?.value?.trim() ?? '';
        if (!timeZoneId) {
            showToast('タイムゾーンIDを選択してください。', false);
            return;
        }
        try {
            const requestBody = { timeZoneId };
            await postData(API_ENDPOINTS.SETTING_EXTERNAL_IMPORT_TIMEZONE, requestBody);
            showToast('保存しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    storageLowSpaceThresholdUpdateButton?.addEventListener('click', async () => {
        const thresholdInput = document.getElementById('storage-low-space-threshold-mb-value');
        const thresholdMb = Number.parseInt(thresholdInput?.value ?? '', 10);
        const maxThresholdMb = 2147483647;
        if (Number.isNaN(thresholdMb) || thresholdMb <= 0) {
            showToast('しきい値は1以上の数値（MB）で入力してください。', false);
            return;
        }
        if (thresholdMb > maxThresholdMb) {
            showToast(`しきい値は ${maxThresholdMb} MB以下で入力してください。`, false);
            return;
        }
        try {
            const requestBody = { thresholdMb };
            await postData(API_ENDPOINTS.SETTING_STORAGE_LOW_SPACE_THRESHOLD, requestBody);
            showToast('保存しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    const saveMonitoringAdvancedSettings = async () => {
        const logRetentionDays = parseNumberInputByIds([
            'log-retention-days-value',
            'log-retention-days-value-desktop'
        ]);
        const storageLowSpaceCheckIntervalMinutes = parseNumberInputByIds([
            'storage-low-space-check-interval-minutes-value',
            'storage-low-space-check-interval-minutes-value-desktop'
        ]);
        const storageLowSpaceNotificationCooldownHours = parseNumberInputByIds([
            'storage-low-space-notification-cooldown-hours-value',
            'storage-low-space-notification-cooldown-hours-value-desktop'
        ]);
        if (Number.isNaN(logRetentionDays) || logRetentionDays < 1 || logRetentionDays > 3650) {
            showToast('ログ保持日数は 1〜3650 日で入力してください。', false);
            return;
        }
        if (Number.isNaN(storageLowSpaceCheckIntervalMinutes) || storageLowSpaceCheckIntervalMinutes < 1 || storageLowSpaceCheckIntervalMinutes > 1440) {
            showToast('空き容量チェック間隔は 1〜1440 分で入力してください。', false);
            return;
        }
        if (Number.isNaN(storageLowSpaceNotificationCooldownHours) || storageLowSpaceNotificationCooldownHours < 1 || storageLowSpaceNotificationCooldownHours > 168) {
            showToast('通知クールダウンは 1〜168 時間で入力してください。', false);
            return;
        }
        try {
            const requestBody = {
                logRetentionDays,
                storageLowSpaceCheckIntervalMinutes,
                storageLowSpaceNotificationCooldownHours
            };
            await postData(API_ENDPOINTS.SETTING_MONITORING_ADVANCED, requestBody);
            showToast('保存しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    };
    updateMonitoringAdvancedBtn?.addEventListener('click', saveMonitoringAdvancedSettings);
    updateMonitoringAdvancedBtnDesktop?.addEventListener('click', saveMonitoringAdvancedSettings);
    const saveClockSkewMonitoringSettings = async () => {
        const enabledInput = getPreferredElementByIds([
            'clock-skew-monitoring-enabled',
            'clock-skew-monitoring-enabled-desktop'
        ]);
        const checkIntervalHours = parseNumberInputByIds([
            'clock-skew-check-interval-hours-value',
            'clock-skew-check-interval-hours-value-desktop'
        ]);
        const thresholdSeconds = parseNumberInputByIds([
            'clock-skew-threshold-seconds-value',
            'clock-skew-threshold-seconds-value-desktop'
        ]);
        const enabled = enabledInput?.checked ?? false;
        if (Number.isNaN(checkIntervalHours) || checkIntervalHours < 1 || checkIntervalHours > 168) {
            showToast('監視間隔は 1〜168 時間で入力してください。', false);
            return;
        }
        if (Number.isNaN(thresholdSeconds) || thresholdSeconds < 1 || thresholdSeconds > 600) {
            showToast('ずれしきい値は 1〜600 秒で入力してください。', false);
            return;
        }
        try {
            const requestBody = {
                enabled,
                checkIntervalHours,
                thresholdSeconds
            };
            await postData(API_ENDPOINTS.SETTING_CLOCK_SKEW_MONITORING, requestBody);
            showToast('保存しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    };
    updateClockSkewMonitoringBtn?.addEventListener('click', saveClockSkewMonitoringSettings);
    updateClockSkewMonitoringBtnDesktop?.addEventListener('click', saveClockSkewMonitoringSettings);
    mergeTagsFromMatchedRulesUpdateButton?.addEventListener('click', async () => {
        const enabledInput = document.getElementById('merge-tags-from-matched-rules-enabled');
        const enabled = enabledInput?.checked ?? false;
        try {
            const requestBody = { enabled };
            await postData(API_ENDPOINTS.SETTING_MERGE_TAGS_FROM_MATCHED_RULES, requestBody);
            showToast('保存しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    embedProgramImageOnRecordUpdateButton?.addEventListener('click', async () => {
        const enabledInput = document.getElementById('embed-program-image-on-record');
        const enabled = enabledInput?.checked ?? false;
        try {
            const requestBody = { enabled };
            await postData(API_ENDPOINTS.SETTING_EMBED_PROGRAM_IMAGE_ON_RECORD, requestBody);
            showToast('保存しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    resumePlaybackAcrossPagesUpdateButton?.addEventListener('click', async () => {
        const enabledInput = document.getElementById('resume-playback-across-pages');
        const enabled = enabledInput?.checked ?? false;
        try {
            const requestBody = { enabled };
            await postData(API_ENDPOINTS.SETTING_RESUME_PLAYBACK_ACROSS_PAGES, requestBody);
            showToast('保存しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    releaseCheckIntervalUpdateButton?.addEventListener('click', async () => {
        const intervalSelect = document.getElementById('release-check-interval-days');
        const intervalDays = Number.parseInt(intervalSelect?.value ?? '', 10);
        const allowedIntervals = new Set([0, 1, 7, 30]);
        if (!allowedIntervals.has(intervalDays)) {
            showToast('チェック間隔は 0, 1, 7, 30 のいずれかを選択してください。', false);
            return;
        }
        try {
            const requestBody = { intervalDays };
            await postData(API_ENDPOINTS.SETTING_RELEASE_CHECK_INTERVAL, requestBody);
            showToast('保存しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    });
    const saveDuplicateDetectionInterval = async () => {
        const enabledInput = getPreferredElementByIds([
            'duplicate-detection-enabled',
            'duplicate-detection-enabled-desktop'
        ]);
        const dayOfWeek = parseNumberInputByIds([
            'duplicate-detection-day-of-week',
            'duplicate-detection-day-of-week-desktop'
        ]);
        const timeInput = getPreferredElementByIds([
            'duplicate-detection-time',
            'duplicate-detection-time-desktop'
        ]);
        const enabled = enabledInput?.checked ?? false;
        const timeText = (timeInput?.value ?? '').trim();
        if (![0, 1, 2, 3, 4, 5, 6].includes(dayOfWeek)) {
            showToast('実行曜日を正しく選択してください。', false);
            return;
        }
        if (!/^\d{2}:\d{2}$/.test(timeText)) {
            showToast('実行時刻を入力してください。', false);
            return;
        }
        const [hourText, minuteText] = timeText.split(':');
        const hour = Number.parseInt(hourText, 10);
        const minute = Number.parseInt(minuteText, 10);
        if (Number.isNaN(hour) || hour < 0 || hour > 23 || Number.isNaN(minute) || minute < 0 || minute > 59) {
            showToast('実行時刻が不正です。', false);
            return;
        }
        try {
            const requestBody = {
                enabled,
                dayOfWeek,
                hour,
                minute
            };
            await postData(API_ENDPOINTS.SETTING_DUPLICATE_DETECTION_INTERVAL, requestBody);
            showToast('保存しました。');
        }
        catch (error) {
            const message = error instanceof Error ? error.message : `${error}`;
            showToast(message, false);
        }
    };
    duplicateDetectionIntervalUpdateButton?.addEventListener('click', saveDuplicateDetectionInterval);
    duplicateDetectionIntervalUpdateButtonDesktop?.addEventListener('click', saveDuplicateDetectionInterval);
    const appendTokenToInput = (inputId, token) => {
        const input = document.getElementById(inputId);
        if (!input || !token) {
            return;
        }
        input.value += token;
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.focus();
        const end = input.value.length;
        input.setSelectionRange(end, end);
    };
    const tokenTargets = [
        { cardId: 'directory-explanation-card', inputId: 'record-directory-path-value' },
        { cardId: 'file-name-explanation-card', inputId: 'RecordFileNameTemplateValue' }
    ];
    tokenTargets.forEach(({ cardId, inputId }) => {
        const card = document.getElementById(cardId);
        if (!card) {
            return;
        }
        const tokenCodes = card.querySelectorAll('.rk-token-help code');
        tokenCodes.forEach((codeElm) => {
            const token = codeElm.textContent?.trim() ?? '';
            if (!token.startsWith('$') || !token.endsWith('$')) {
                return;
            }
            codeElm.classList.add('cursor-pointer');
            codeElm.title = 'クリックで追加';
            codeElm.addEventListener('click', () => {
                appendTokenToInput(inputId, token);
            });
        });
    });
    const explanationCards = document.getElementsByClassName('toggle-card');
    Array.from(explanationCards).forEach((card) => {
        const header = card.querySelector('.card-header');
        const content = card.querySelector('.card-content');
        const icon = card.querySelector('.card-header-icon i');
        if (header && content) {
            header.addEventListener('click', () => {
                content.classList.toggle('hidden');
                if (icon) {
                    icon.classList.toggle('fa-angle-down');
                    icon.classList.toggle('fa-angle-up');
                }
            });
        }
    });
    window.addEventListener('beforeunload', () => {
        if (programUpdateHubConnection) {
            void programUpdateHubConnection.stop();
            programUpdateHubConnection = null;
        }
    });
});
//# sourceMappingURL=setting.js.map