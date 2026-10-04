import { API_ENDPOINTS } from './const.js';
import { showConfirmDialog } from './feedback.js';
import { escapeHtml } from './utils.js';
import { playerPlaybackRateOptions } from './player-rate-control.js';
import { createStandardPlayerJumpControls } from './player-jump-controls.js';
import { configurePlayer, playPlayerSource, stopPlayer } from './player-controller.js';
import { registerPage } from './page-navigation.js';
const playerStartOffsetMinutesOptions = [0, 1, 2, 3, 5, 10];
const playerStartOffsetStorageKey = 'radicorder-player-start-offset-minutes';
registerPage('recorded-duplicates.js', (signal) => {
    const verificationToken = document.getElementById('VerificationToken')?.value ?? '';
    const runButton = document.getElementById('duplicate-run-btn');
    const lookbackDaysSelect = document.getElementById('duplicate-lookback-days');
    const maxGroupsSelect = document.getElementById('duplicate-max-groups');
    const phase2ModeSelect = document.getElementById('duplicate-phase2-mode');
    const clusterWindowHoursSelect = document.getElementById('duplicate-cluster-window-hours');
    const startOffsetMinutesSelect = document.getElementById('duplicate-start-offset-minutes');
    const loading = document.getElementById('duplicate-loading');
    const errorBox = document.getElementById('duplicate-error');
    const statusBox = document.getElementById('duplicate-status');
    const lastStarted = document.getElementById('duplicate-last-started');
    const lastCompleted = document.getElementById('duplicate-last-completed');
    const lastMessage = document.getElementById('duplicate-last-message');
    const tableBody = document.getElementById('duplicate-table-body');
    const selectAllCheckbox = document.getElementById('duplicate-select-all-checkbox');
    const selectAllButton = document.getElementById('duplicate-select-all-btn');
    const clearSelectionButton = document.getElementById('duplicate-clear-selection-btn');
    const bulkDeleteButton = document.getElementById('duplicate-bulk-delete-btn');
    const selectedCount = document.getElementById('duplicate-selected-count');
    if (!runButton || !lookbackDaysSelect || !maxGroupsSelect || !phase2ModeSelect || !clusterWindowHoursSelect || !startOffsetMinutesSelect || !loading || !errorBox || !statusBox || !lastStarted || !lastCompleted || !lastMessage || !tableBody || !selectAllCheckbox || !selectAllButton || !clearSelectionButton || !bulkDeleteButton || !selectedCount) {
        return;
    }
    let lastRunningState = false;
    let candidates = [];
    let groups = [];
    const selectedRecordingIds = new Set();
    let currentPlayingRecordingId = null;
    let pollTimer = null;
    let duplicateHubConnection = null;
    const isCurrentDuplicateRecordingPlaying = (recordId) => {
        return currentPlayingRecordingId === recordId;
    };
    const setDuplicatePlayButtonState = (button, isPlaying) => {
        if (isPlaying) {
            button.textContent = '停止';
            button.classList.remove('is-primary');
            button.classList.add('is-danger');
            button.setAttribute('aria-label', '停止');
            return;
        }
        button.textContent = '再生';
        button.classList.remove('is-danger');
        button.classList.add('is-primary');
        button.setAttribute('aria-label', '再生');
    };
    const syncDuplicatePlayButtons = () => {
        const buttons = document.querySelectorAll('.duplicate-play-member[data-record-id]');
        buttons.forEach((button) => {
            const recordId = button.dataset.recordId ?? '';
            setDuplicatePlayButtonState(button, isCurrentDuplicateRecordingPlaying(recordId));
        });
    };
    configurePlayer({
        createControls: createPlayerJumpControls,
        onStateChanged: (state) => {
            currentPlayingRecordingId = state?.recordId ?? null;
            syncDuplicatePlayButtons();
        }
    });
    const restoreStartOffsetSelection = () => {
        const current = getPlayerStartOffsetMinutes();
        const found = Array.from(startOffsetMinutesSelect.options).some(option => Number.parseInt(option.value, 10) === current);
        startOffsetMinutesSelect.value = found ? current.toString() : '0';
    };
    startOffsetMinutesSelect.addEventListener('change', () => {
        const parsed = Number.parseInt(startOffsetMinutesSelect.value, 10);
        const next = Number.isFinite(parsed) && playerStartOffsetMinutesOptions.includes(parsed) ? parsed : 0;
        localStorage.setItem(playerStartOffsetStorageKey, next.toString());
    });
    restoreStartOffsetSelection();
    const setLoading = (isLoading) => {
        runButton.disabled = isLoading;
        loading.classList.toggle('hidden', !isLoading);
    };
    const setError = (message) => {
        errorBox.textContent = message;
        errorBox.classList.toggle('hidden', message.length === 0);
    };
    const formatDateTime = (raw) => {
        if (!raw) {
            return '-';
        }
        const date = new Date(raw);
        if (Number.isNaN(date.getTime())) {
            return '-';
        }
        return `${date.getFullYear()}/${String(date.getMonth() + 1).padStart(2, '0')}/${String(date.getDate()).padStart(2, '0')} ${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}:${String(date.getSeconds()).padStart(2, '0')}`;
    };
    const formatShortDateTime = (raw) => {
        const date = new Date(raw);
        if (Number.isNaN(date.getTime())) {
            return '-';
        }
        return `${date.getFullYear()}/${String(date.getMonth() + 1).padStart(2, '0')}/${String(date.getDate()).padStart(2, '0')} ${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;
    };
    const formatDuration = (durationSeconds) => {
        const sec = Math.max(0, Math.floor(durationSeconds));
        const h = Math.floor(sec / 3600);
        const m = Math.floor((sec % 3600) / 60);
        const s = sec % 60;
        if (h > 0) {
            return `${h}時間${m}分${s}秒`;
        }
        return `${m}分${s}秒`;
    };
    const playRecording = (recordId) => {
        if (isCurrentDuplicateRecordingPlaying(recordId)) {
            stopPlayer();
            return;
        }
        const recording = groups.flatMap(group => group.members).find(member => member.recordingId === recordId);
        void playPlayerSource({
            sourceUrl: `/api/recordings/play/${recordId}`,
            kind: 'recording',
            recordId,
            title: recording?.title ?? null,
            currentTime: getPlayerStartOffsetSeconds()
        });
    };
    const buildGroups = (items) => {
        const nodeMap = new Map();
        const parent = new Map();
        const rank = new Map();
        const addNode = (node, score) => {
            const existing = nodeMap.get(node.recordingId);
            if (!existing) {
                nodeMap.set(node.recordingId, { ...node, bestScore: score });
                parent.set(node.recordingId, node.recordingId);
                rank.set(node.recordingId, 0);
                return;
            }
            if (score > existing.bestScore) {
                existing.bestScore = score;
            }
        };
        const find = (x) => {
            const p = parent.get(x) ?? x;
            if (p === x) {
                return x;
            }
            const root = find(p);
            parent.set(x, root);
            return root;
        };
        const union = (a, b) => {
            const rootA = find(a);
            const rootB = find(b);
            if (rootA === rootB) {
                return;
            }
            const rankA = rank.get(rootA) ?? 0;
            const rankB = rank.get(rootB) ?? 0;
            if (rankA < rankB) {
                parent.set(rootA, rootB);
                return;
            }
            if (rankA > rankB) {
                parent.set(rootB, rootA);
                return;
            }
            parent.set(rootB, rootA);
            rank.set(rootA, rankA + 1);
        };
        items.forEach((item) => {
            addNode(item.left, item.finalScore);
            addNode(item.right, item.finalScore);
            union(item.left.recordingId, item.right.recordingId);
        });
        const groupsByRoot = new Map();
        nodeMap.forEach((member, id) => {
            const root = find(id);
            const group = groupsByRoot.get(root) ?? {
                groupKey: '',
                representativeTitle: '',
                memberCount: 0,
                maxFinalScore: 0,
                members: []
            };
            group.members.push(member);
            groupsByRoot.set(root, group);
        });
        items.forEach((item) => {
            const root = find(item.left.recordingId);
            const group = groupsByRoot.get(root);
            if (!group) {
                return;
            }
            group.maxFinalScore = Math.max(group.maxFinalScore, item.finalScore);
        });
        const result = Array.from(groupsByRoot.values())
            .map((group) => {
            const sortedMembers = group.members
                .slice()
                .sort((a, b) => new Date(a.startDateTime).getTime() - new Date(b.startDateTime).getTime());
            const groupKey = sortedMembers.map(x => x.recordingId).join('|');
            return {
                groupKey,
                representativeTitle: sortedMembers[0]?.title ?? '-',
                memberCount: sortedMembers.length,
                maxFinalScore: group.maxFinalScore,
                members: sortedMembers
            };
        })
            .sort((a, b) => {
            if (b.maxFinalScore !== a.maxFinalScore) {
                return b.maxFinalScore - a.maxFinalScore;
            }
            if (b.memberCount !== a.memberCount) {
                return b.memberCount - a.memberCount;
            }
            return (new Date(a.members[0]?.startDateTime ?? 0).getTime()) - (new Date(b.members[0]?.startDateTime ?? 0).getTime());
        });
        return result;
    };
    const updateSelectionState = () => {
        const allMembers = groups.flatMap(group => group.members);
        selectedCount.textContent = `${selectedRecordingIds.size}件選択中`;
        const allChecked = allMembers.length > 0 && allMembers.every(item => selectedRecordingIds.has(item.recordingId));
        selectAllCheckbox.checked = allChecked;
        bulkDeleteButton.disabled = selectedRecordingIds.size === 0;
    };
    const renderGroups = () => {
        tableBody.innerHTML = '';
        if (groups.length === 0) {
            const tr = document.createElement('tr');
            tr.innerHTML = '<td colspan="3">チェック結果はまだありません。抽出実行後に同一番組候補グループが表示されます。</td>';
            tableBody.appendChild(tr);
            updateSelectionState();
            return;
        }
        groups.forEach((group) => {
            const tr = document.createElement('tr');
            const membersHtml = group.members.map((member) => `
                <div class="duplicate-group-member">
                    <div class="duplicate-group-member-actions">
                        <label class="checkbox">
                            <input type="checkbox" class="duplicate-member-checkbox" data-record-id="${escapeHtml(member.recordingId ?? '')}" ${selectedRecordingIds.has(member.recordingId) ? 'checked' : ''}>
                        </label>
                    </div>
                    <div class="duplicate-group-member-main">
                        <div class="duplicate-side-title">${escapeHtml(member.title ?? '')}</div>
                        <div class="duplicate-side-meta">${escapeHtml(member.stationName ?? '')}</div>
                        <div class="duplicate-side-meta">${escapeHtml(formatShortDateTime(member.startDateTime))} / ${escapeHtml(formatDuration(member.durationSeconds))}</div>
                    </div>
                    <div class="duplicate-group-member-actions">
                        <button type="button" class="button is-small is-primary duplicate-play-member" data-record-id="${escapeHtml(member.recordingId ?? '')}">再生</button>
                    </div>
                </div>
            `).join('');
            tr.innerHTML = `
                <td>
                    <label class="checkbox">
                        <input type="checkbox" class="duplicate-group-bulk-checkbox">
                    </label>
                </td>
                <td>
                    <div class="duplicate-group-title">${escapeHtml(group.representativeTitle ?? '')}</div>
                    <div class="duplicate-group-meta">${escapeHtml(group.memberCount.toString())}件</div>
                </td>
                <td>
                    <div class="duplicate-group-members">
                        ${membersHtml}
                    </div>
                </td>
            `;
            const groupCheckbox = tr.querySelector('.duplicate-group-bulk-checkbox');
            const memberCheckboxes = Array.from(tr.querySelectorAll('.duplicate-member-checkbox'));
            const syncGroupCheckbox = () => {
                const checkedCount = memberCheckboxes.filter(x => x.checked).length;
                groupCheckbox && (groupCheckbox.checked = checkedCount > 0 && checkedCount === memberCheckboxes.length);
            };
            memberCheckboxes.forEach((checkbox) => {
                checkbox.addEventListener('change', () => {
                    const recordId = checkbox.dataset.recordId;
                    if (!recordId) {
                        return;
                    }
                    if (checkbox.checked) {
                        selectedRecordingIds.add(recordId);
                    }
                    else {
                        selectedRecordingIds.delete(recordId);
                    }
                    syncGroupCheckbox();
                    updateSelectionState();
                });
            });
            groupCheckbox?.addEventListener('change', () => {
                memberCheckboxes.forEach((checkbox) => {
                    const recordId = checkbox.dataset.recordId;
                    if (!recordId) {
                        return;
                    }
                    checkbox.checked = groupCheckbox.checked;
                    if (groupCheckbox.checked) {
                        selectedRecordingIds.add(recordId);
                    }
                    else {
                        selectedRecordingIds.delete(recordId);
                    }
                });
                updateSelectionState();
            });
            syncGroupCheckbox();
            tr.querySelectorAll('.duplicate-play-member').forEach((elm) => {
                const button = elm;
                const recordId = button.dataset.recordId;
                if (!recordId) {
                    return;
                }
                setDuplicatePlayButtonState(button, isCurrentDuplicateRecordingPlaying(recordId));
                button.addEventListener('click', () => playRecording(recordId));
            });
            tableBody.appendChild(tr);
        });
        updateSelectionState();
        syncDuplicatePlayButtons();
    };
    const fetchCandidates = async () => {
        const response = await fetch(API_ENDPOINTS.PROGRAM_RECORDED_DUPLICATES_CANDIDATES, {
            method: 'GET',
            headers: {
                'Content-Type': 'application/json'
            }
        });
        const result = await response.json();
        if (!response.ok || !result.success) {
            throw new Error(result.error?.message ?? '同一番組候補一覧の取得に失敗しました。');
        }
        candidates = result.data ?? [];
        groups = buildGroups(candidates);
        selectedRecordingIds.clear();
        renderGroups();
    };
    const renderStatus = (status) => {
        statusBox.classList.toggle('is-warning', status.isRunning);
        statusBox.classList.toggle('is-success', !status.isRunning && status.lastSucceeded);
        statusBox.classList.toggle('is-light', !status.isRunning && !status.lastSucceeded);
        lastStarted.textContent = formatDateTime(status.lastStartedAtUtc);
        lastCompleted.textContent = formatDateTime(status.lastCompletedAtUtc);
        lastMessage.textContent = status.lastMessage || (status.isRunning ? '抽出処理を実行中です。' : '-');
    };
    const fetchStatus = async () => {
        const response = await fetch(API_ENDPOINTS.PROGRAM_RECORDED_DUPLICATES_STATUS, {
            method: 'GET',
            headers: {
                'Content-Type': 'application/json'
            }
        });
        const result = await response.json();
        if (!response.ok || !result.success || !result.data) {
            throw new Error(result.error?.message ?? '状態取得に失敗しました。');
        }
        renderStatus(result.data);
        setLoading(result.data.isRunning);
        if (lastRunningState && !result.data.isRunning) {
            await fetchCandidates();
        }
        lastRunningState = result.data.isRunning;
    };
    const startPolling = () => {
        if (pollTimer !== null) {
            window.clearInterval(pollTimer);
        }
        pollTimer = window.setInterval(async () => {
            try {
                await fetchStatus();
            }
            catch {
                // polling中の一時エラーは表示しない
            }
        }, 5000);
    };
    /**
     * 同一番組候補チェック状態のSignalR接続を初期化する
     */
    const initializeDuplicateDetectionHubConnectionAsync = async () => {
        const signalRNamespace = window.signalR;
        if (!signalRNamespace) {
            return;
        }
        const connection = new signalRNamespace.HubConnectionBuilder()
            .withUrl('/hubs/duplicate-detection')
            .withAutomaticReconnect()
            .configureLogging(signalRNamespace.LogLevel.Warning)
            .build();
        connection.on('duplicateDetectionStatusChanged', (...args) => {
            const status = (args[0] ?? null);
            if (!status) {
                return;
            }
            renderStatus(status);
            setLoading(status.isRunning);
            if (lastRunningState && !status.isRunning) {
                void fetchCandidates();
            }
            lastRunningState = status.isRunning;
        });
        connection.onreconnected(() => {
            void fetchStatus();
        });
        try {
            await connection.start();
            duplicateHubConnection = connection;
        }
        catch {
            // 接続失敗時はポーリングのみで継続する
        }
    };
    runButton.addEventListener('click', async () => {
        setError('');
        setLoading(true);
        try {
            const lookbackDaysValue = Number.parseInt(lookbackDaysSelect.value, 10);
            const maxPhase1GroupsValue = Number.parseInt(maxGroupsSelect.value, 10);
            const broadcastClusterWindowHoursValue = Number.parseInt(clusterWindowHoursSelect.value, 10);
            const requestBody = {
                lookbackDays: Number.isNaN(lookbackDaysValue) ? 30 : lookbackDaysValue,
                maxPhase1Groups: Number.isNaN(maxPhase1GroupsValue) ? 100 : maxPhase1GroupsValue,
                phase2Mode: phase2ModeSelect.value || 'light',
                broadcastClusterWindowHours: Number.isNaN(broadcastClusterWindowHoursValue) ? 48 : broadcastClusterWindowHoursValue
            };
            const response = await fetch(API_ENDPOINTS.PROGRAM_RECORDED_DUPLICATES_RUN, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': verificationToken
                },
                body: JSON.stringify(requestBody)
            });
            const result = await response.json();
            if (!response.ok || !result.success) {
                throw new Error(result.error?.message ?? result.message ?? '同一番組候補チェックジョブの開始に失敗しました。');
            }
            await fetchStatus();
            await fetchCandidates();
        }
        catch (error) {
            setLoading(false);
            const message = error instanceof Error ? error.message : '同一番組候補チェックジョブの開始に失敗しました。';
            setError(message);
        }
    });
    selectAllCheckbox.addEventListener('change', () => {
        const allMembers = groups.flatMap(group => group.members);
        if (selectAllCheckbox.checked) {
            allMembers.forEach(member => selectedRecordingIds.add(member.recordingId));
        }
        else {
            selectedRecordingIds.clear();
        }
        renderGroups();
    });
    selectAllButton.addEventListener('click', () => {
        groups.flatMap(group => group.members).forEach(member => selectedRecordingIds.add(member.recordingId));
        renderGroups();
    });
    clearSelectionButton.addEventListener('click', () => {
        selectedRecordingIds.clear();
        renderGroups();
    });
    bulkDeleteButton.addEventListener('click', async () => {
        if (selectedRecordingIds.size === 0) {
            return;
        }
        const confirmed = await showConfirmDialog(`選択した${selectedRecordingIds.size}件の録音を削除します。\nこの操作は元に戻せません。`, { title: '録音の一括削除', okText: '削除する' });
        if (!confirmed) {
            return;
        }
        const recordingIds = Array.from(selectedRecordingIds);
        const requestBody = {
            recordingIds,
            deleteFiles: true
        };
        try {
            const response = await fetch(API_ENDPOINTS.DELETE_PROGRAM_BULK, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': verificationToken
                },
                body: JSON.stringify(requestBody)
            });
            const result = await response.json();
            if (!response.ok || !result.success) {
                throw new Error(result.error?.message ?? result.message ?? '削除に失敗しました。');
            }
            const deletedIds = new Set(recordingIds);
            candidates = candidates.filter(item => !deletedIds.has(item.left.recordingId) && !deletedIds.has(item.right.recordingId));
            groups = buildGroups(candidates);
            selectedRecordingIds.clear();
            renderGroups();
        }
        catch (error) {
            const message = error instanceof Error ? error.message : '削除に失敗しました。';
            setError(message);
        }
    });
    void fetchStatus().catch((error) => {
        const message = error instanceof Error ? error.message : '状態取得に失敗しました。';
        setError(message);
    });
    void fetchCandidates().catch((error) => {
        const message = error instanceof Error ? error.message : '同一番組候補一覧の取得に失敗しました。';
        setError(message);
    });
    startPolling();
    void initializeDuplicateDetectionHubConnectionAsync().then(() => {
        if (signal.aborted) {
            cleanup();
        }
    });
    const cleanup = () => {
        if (pollTimer !== null) {
            window.clearInterval(pollTimer);
            pollTimer = null;
        }
        if (duplicateHubConnection) {
            void duplicateHubConnection.stop();
            duplicateHubConnection = null;
        }
    };
    window.addEventListener('beforeunload', cleanup);
    return () => {
        window.removeEventListener('beforeunload', cleanup);
        cleanup();
    };
});
function createPlayerJumpControls(audioElm) {
    return createStandardPlayerJumpControls(audioElm, {
        playbackRateOptions: playerPlaybackRateOptions
    });
}
function getPlayerStartOffsetMinutes() {
    const raw = localStorage.getItem(playerStartOffsetStorageKey);
    const parsed = raw ? Number.parseInt(raw, 10) : NaN;
    if (Number.isFinite(parsed) && playerStartOffsetMinutesOptions.includes(parsed)) {
        return parsed;
    }
    return playerStartOffsetMinutesOptions[0];
}
function getPlayerStartOffsetSeconds() {
    return getPlayerStartOffsetMinutes() * 60;
}
//# sourceMappingURL=recorded-duplicates.js.map