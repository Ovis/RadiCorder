import { ApiResponseContract, ScheduleEntryResponseContract as ProgramReserve, RadioProgramResponseContract as Program } from './openapi-response-contract.js';
import { API_ENDPOINTS } from './const.js';
import { RadioServiceKind, ReserveType, RecordingType, RecordingTypeMap, ReserveTypeMap } from './define.js';
import type { ReserveEntryRequestContract } from './openapi-contract.js';
import { setTextContent, setEventListener, setInnerHtml, sanitizeHtml } from './utils.js';
import { createInlineToast, wireInlineToastClose } from './inline-toast.js';
import type { SignalRHubConnection, SignalRWindow } from './signalr-types.js';

let recordingsCache: ProgramReserve[] = [];
type SortKey = 'title' | 'time' | 'recordingType' | 'reserveType' | 'status';
type SortDirection = 'asc' | 'desc';
let currentSortKey: SortKey = 'time';
let currentSortDirection: SortDirection = 'asc';
const showToast = createInlineToast('program-reserve-result-toast', 'program-reserve-result-toast-message');
let reserveHubConnection: SignalRHubConnection | null = null;
let isRealtimeReloadRunning = false;
let hasRealtimeReloadPending = false;
// API由来の録音種別値を表示文字列へ変換する。
const getRecordingTypeDisplayName = (value: string | number | null | undefined): string =>
    RecordingTypeMap[Number(value) as keyof typeof RecordingTypeMap]?.displayName ?? '未定義';
// API由来の予約種別値を表示文字列へ変換する。
const getReserveTypeDisplayName = (value: string | number | null | undefined): string =>
    ReserveTypeMap[Number(value) as keyof typeof ReserveTypeMap]?.displayName ?? '未定義';

const getSortSelectValue = (sortKey: SortKey, direction: SortDirection): string => `${sortKey}_${direction}`;

const applySortSelectValue = (value: string): void => {
    const [sortKey, direction] = value.split('_');
    const validSortKey = ['title', 'time', 'recordingType', 'reserveType', 'status'].includes(sortKey);
    const validDirection = direction === 'asc' || direction === 'desc';
    if (!validSortKey || !validDirection) {
        return;
    }

    currentSortKey = sortKey as SortKey;
    currentSortDirection = direction as SortDirection;
};

function showConfirmDialog(message: string): Promise<boolean> {
    return new Promise((resolve) => {
        const modal = document.createElement('div');
        modal.className = 'modal is-active';
        modal.id = 'program-reserve-confirm-modal';

        modal.innerHTML = `
            <div class="modal-background"></div>
            <div class="modal-card" style="max-width: 28rem;">
                <header class="modal-card-head">
                    <p class="modal-card-title">確認</p>
                    <button class="delete" aria-label="close"></button>
                </header>
                <section class="modal-card-body">
                    <p>${message}</p>
                </section>
                <footer class="modal-card-foot">
                    <div class="buttons">
                        <button class="button is-danger" data-action="ok">削除する</button>
                        <button class="button is-light" data-action="cancel">キャンセル</button>
                    </div>
                </footer>
            </div>
        `;

        const close = (result: boolean) => {
            modal.remove();
            resolve(result);
        };

        (modal.querySelector('.modal-background') as HTMLElement).addEventListener('click', () => close(false));
        (modal.querySelector('.delete') as HTMLElement).addEventListener('click', () => close(false));
        (modal.querySelector('[data-action="cancel"]') as HTMLElement).addEventListener('click', () => close(false));
        (modal.querySelector('[data-action="ok"]') as HTMLElement).addEventListener('click', () => close(true));

        document.body.appendChild(modal);
    });
}

document.addEventListener('DOMContentLoaded', async () => {
    wireInlineToastClose('program-reserve-result-toast-close', 'program-reserve-result-toast');
    const sortTitleButton = document.getElementById('sort-title') as HTMLAnchorElement | null;
    const sortTimeButton = document.getElementById('sort-start') as HTMLAnchorElement | null;
    const sortRecordingTypeButton = document.getElementById('sort-recording-type') as HTMLAnchorElement | null;
    const sortReserveTypeButton = document.getElementById('sort-reserve-type') as HTMLAnchorElement | null;
    const sortStatusButton = document.getElementById('sort-status') as HTMLAnchorElement | null;
    const mobileSortSelect = document.getElementById('mobile-sort-select') as HTMLSelectElement | null;

    sortTitleButton?.addEventListener('click', (event) => {
        event.preventDefault();
        if (currentSortKey === 'title') {
            currentSortDirection = currentSortDirection === 'asc' ? 'desc' : 'asc';
        } else {
            currentSortKey = 'title';
            currentSortDirection = 'asc';
        }
        renderRecordings(recordingsCache);
    });

    sortTimeButton?.addEventListener('click', (event) => {
        event.preventDefault();
        if (currentSortKey === 'time') {
            currentSortDirection = currentSortDirection === 'asc' ? 'desc' : 'asc';
        } else {
            currentSortKey = 'time';
            currentSortDirection = 'asc';
        }
        renderRecordings(recordingsCache);
    });

    sortRecordingTypeButton?.addEventListener('click', (event) => {
        event.preventDefault();
        if (currentSortKey === 'recordingType') {
            currentSortDirection = currentSortDirection === 'asc' ? 'desc' : 'asc';
        } else {
            currentSortKey = 'recordingType';
            currentSortDirection = 'asc';
        }
        renderRecordings(recordingsCache);
    });

    sortReserveTypeButton?.addEventListener('click', (event) => {
        event.preventDefault();
        if (currentSortKey === 'reserveType') {
            currentSortDirection = currentSortDirection === 'asc' ? 'desc' : 'asc';
        } else {
            currentSortKey = 'reserveType';
            currentSortDirection = 'asc';
        }
        renderRecordings(recordingsCache);
    });

    sortStatusButton?.addEventListener('click', (event) => {
        event.preventDefault();
        if (currentSortKey === 'status') {
            currentSortDirection = currentSortDirection === 'asc' ? 'desc' : 'asc';
        } else {
            currentSortKey = 'status';
            currentSortDirection = 'asc';
        }
        renderRecordings(recordingsCache);
    });

    if (mobileSortSelect) {
        mobileSortSelect.value = getSortSelectValue(currentSortKey, currentSortDirection);
        mobileSortSelect.addEventListener('change', () => {
            applySortSelectValue(mobileSortSelect.value);
            renderRecordings(recordingsCache);
        });
    }

    await loadRecordings();
    await initializeReserveHubConnectionAsync();

    window.addEventListener('beforeunload', () => {
        if (reserveHubConnection) {
            void reserveHubConnection.stop();
            reserveHubConnection = null;
        }
    });
});

/**
 * SignalR経由の変更通知を受けた際に予約一覧を再同期する
 */
const reloadReservesFromRealtimeAsync = async (): Promise<void> => {
    if (isRealtimeReloadRunning) {
        hasRealtimeReloadPending = true;
        return;
    }

    isRealtimeReloadRunning = true;
    try {
        do {
            hasRealtimeReloadPending = false;
            await loadRecordings();
        } while (hasRealtimeReloadPending);
    } finally {
        isRealtimeReloadRunning = false;
    }
};

/**
 * 録音予定更新のSignalR接続を初期化する
 */
const initializeReserveHubConnectionAsync = async (): Promise<void> => {
    const signalRNamespace = (window as SignalRWindow).signalR;
    if (!signalRNamespace) {
        console.warn('SignalRクライアントが読み込まれていないため、録音予定Push同期を無効化します。');
        return;
    }

    const connection = new signalRNamespace.HubConnectionBuilder()
        .withUrl('/hubs/reserves')
        .withAutomaticReconnect()
        .configureLogging(signalRNamespace.LogLevel.Warning)
        .build();

    connection.on('reserveScheduleChanged', () => {
        void reloadReservesFromRealtimeAsync();
    });

    connection.onreconnected(() => {
        void reloadReservesFromRealtimeAsync();
    });

    connection.onclose((error) => {
        if (error) {
            console.warn('録音予定SignalR接続が切断されました。', error);
        }
    });

    try {
        await connection.start();
        reserveHubConnection = connection;
    } catch (error) {
        console.warn('録音予定SignalR接続の開始に失敗しました。', error);
    }
};

const loadRecordings = async (): Promise<void> => {
    try {
        localStorage.removeItem('program-reserve-list');

        const response: Response = await fetch(API_ENDPOINTS.RESERVE_PROGRAM_LIST);
        const result = await response.json() as ApiResponseContract<ProgramReserve[]>;
        const data: ProgramReserve[] = result.data ?? [];
        recordingsCache = data;

        localStorage.setItem('program-reserve-list', JSON.stringify(data));

        renderRecordings(data);
    } catch (error) {
        console.error('Error loading recordings:', error);
    }
};

const renderRecordings = (recordings: ProgramReserve[]): void => {
    const tableBody: HTMLElement = document.getElementById('recordings-table-body') as HTMLElement;
    tableBody.innerHTML = '';
    updateSortButtons();

    const template = document.getElementById('recordings-table-template') as HTMLTemplateElement;
    const sortedRecordings = getSortedRecordings(recordings);

    sortedRecordings.forEach((reserve: ProgramReserve) => {
        const row = template.content.cloneNode(true) as HTMLElement;

        setTextContent(row, '.program-title', `[${reserve.stationName}]${reserve.title}`);

        const dateTimeFormatOptions: Intl.DateTimeFormatOptions = {
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit'
        };

        const timeFormatOptions: Intl.DateTimeFormatOptions = {
            hour: '2-digit',
            minute: '2-digit'
        };

        const onAirStartTime: string = new Date(reserve.startDateTime).toLocaleDateString('ja-JP', dateTimeFormatOptions);
        const onAirEndTime: string = new Date(reserve.endDateTime).toLocaleTimeString('ja-JP', timeFormatOptions);
        setTextContent(row, '.onair-date', `${onAirStartTime}～${onAirEndTime}`);

        setTextContent(row, '.recording-type', getRecordingTypeDisplayName(reserve.recordingType));
        setTextContent(row, '.reserve-type', getReserveTypeDisplayName(reserve.reserveType));
        setTextContent(row, '.reserve-status', reserve.isEnabled ? '有効' : '無効');

        setEventListener(row, '.detail-button', 'click', async () => await showDetailModal(reserve));

        if (reserve.reserveType === ReserveType.Program) {
            row.querySelector('.status-button')?.remove();

            const deleteAction = async (): Promise<void> => {
                const requestBody: ReserveEntryRequestContract = {
                    id: reserve.id,
                };

                const confirmed = await showConfirmDialog('削除してもよいですか？');
                if (!confirmed) {
                    return;
                }

                try {
                    const response = await fetch(API_ENDPOINTS.RESERVE_PROGRAM_DELETE, {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json'
                        },
                        body: JSON.stringify(requestBody)
                    });

                    const rawText = await response.text();
                    let result: ApiResponseContract<null> | null = null;
                    if (rawText) {
                        try {
                            result = JSON.parse(rawText) as ApiResponseContract<null>;
                        } catch {
                            result = null;
                        }
                    }

                    const message: string | null | undefined = result?.message;
                    const isSuccess = result?.success === true || (response.ok && result == null);

                    if (isSuccess) {
                        row.remove();
                        showToast(message ?? '削除しました。');
                    } else if ((message ?? '').includes('見つかりません')) {
                        row.remove();
                        showToast('削除しました。');
                    } else {
                        showToast(message ?? '削除に失敗しました。', false);
                    }
                } catch (error) {
                    console.error('Error:', error);
                    await loadRecordings();
                    showToast('削除処理を実行しました。一覧を更新しました。');
                }
            };

            setEventListener(row, '.delete-button', 'click', async () => await deleteAction());
        }
        else if (reserve.reserveType === ReserveType.Keyword) {
            row.querySelector('.delete-button')?.remove();

            row.querySelector('.status-icon')?.classList.add(reserve.isEnabled ? 'fa-ban' : 'fa-check');
            setTextContent(row, '.status-text', reserve.isEnabled ? '無効化' : '有効化');
            const statusButton = row.querySelector('.status-button') as HTMLAnchorElement | null;
            if (statusButton) {
                const label = reserve.isEnabled ? '無効化' : '有効化';
                statusButton.title = label;
                statusButton.setAttribute('aria-label', label);
            }

            const updateStatusAction = async (): Promise<void> => {
                const requestBody: ReserveEntryRequestContract = {
                    id: reserve.id,
                };

                try {
                    const response = await fetch(API_ENDPOINTS.RESERVE_SWITCH_STATUS, {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json'
                        },
                        body: JSON.stringify(requestBody)
                    });

                    const result = await response.json() as ApiResponseContract<null>;

                    if (result.success) {
                        showToast(result.message ?? '更新しました。');
                        await loadRecordings();
                    } else {
                        showToast(result.message ?? 'エラーが発生しました。', false);
                    }
                } catch (error) {
                    console.error('Error:', error);
                    showToast('エラーが発生しました。', false);
                }
            };

            setEventListener(row, '.status-button', 'click', async () => await updateStatusAction());
        }

        tableBody.appendChild(row);
    });
};

const getSortedRecordings = (recordings: ProgramReserve[]): ProgramReserve[] => {
    const multiplier = currentSortDirection === 'asc' ? 1 : -1;
    const byTimeThenTitle = (a: ProgramReserve, b: ProgramReserve): number => {
        const startA = new Date(a.startDateTime).getTime();
        const startB = new Date(b.startDateTime).getTime();
        if (startA !== startB) {
            return (startA - startB) * multiplier;
        }

        return (a.title ?? '').localeCompare((b.title ?? ''), 'ja') * multiplier;
    };

    return [...recordings].sort((a, b) => {
        if (currentSortKey === 'title') {
            const titleCompare = (a.title ?? '').localeCompare((b.title ?? ''), 'ja');
            if (titleCompare !== 0) {
                return titleCompare * multiplier;
            }

            return byTimeThenTitle(a, b);
        }

        if (currentSortKey === 'recordingType') {
            const aText = getRecordingTypeDisplayName(a.recordingType);
            const bText = getRecordingTypeDisplayName(b.recordingType);
            const compare = aText.localeCompare(bText, 'ja');
            if (compare !== 0) {
                return compare * multiplier;
            }
            return byTimeThenTitle(a, b);
        }

        if (currentSortKey === 'reserveType') {
            const aText = getReserveTypeDisplayName(a.reserveType);
            const bText = getReserveTypeDisplayName(b.reserveType);
            const compare = aText.localeCompare(bText, 'ja');
            if (compare !== 0) {
                return compare * multiplier;
            }
            return byTimeThenTitle(a, b);
        }

        if (currentSortKey === 'status') {
            const aText = a.isEnabled ? '有効' : '無効';
            const bText = b.isEnabled ? '有効' : '無効';
            const compare = aText.localeCompare(bText, 'ja');
            if (compare !== 0) {
                return compare * multiplier;
            }
            return byTimeThenTitle(a, b);
        }

        return byTimeThenTitle(a, b);
    });
};

const updateSortButtons = (): void => {
    const sortTitleButton = document.getElementById('sort-title') as HTMLAnchorElement | null;
    const sortTimeButton = document.getElementById('sort-start') as HTMLAnchorElement | null;
    const sortRecordingTypeButton = document.getElementById('sort-recording-type') as HTMLAnchorElement | null;
    const sortReserveTypeButton = document.getElementById('sort-reserve-type') as HTMLAnchorElement | null;
    const sortStatusButton = document.getElementById('sort-status') as HTMLAnchorElement | null;
    const mobileSortSelect = document.getElementById('mobile-sort-select') as HTMLSelectElement | null;

    if (!sortTitleButton || !sortTimeButton || !sortRecordingTypeButton || !sortReserveTypeButton || !sortStatusButton) {
        return;
    }

    [sortTitleButton, sortTimeButton, sortRecordingTypeButton, sortReserveTypeButton, sortStatusButton]
        .forEach((button) => button.classList.remove('sort-up', 'sort-down'));

    const targetButton = currentSortKey === 'title'
        ? sortTitleButton
        : currentSortKey === 'time'
            ? sortTimeButton
            : currentSortKey === 'recordingType'
                ? sortRecordingTypeButton
                : currentSortKey === 'reserveType'
                    ? sortReserveTypeButton
                    : sortStatusButton;
    targetButton.classList.add(currentSortDirection === 'asc' ? 'sort-up' : 'sort-down');

    if (mobileSortSelect) {
        mobileSortSelect.value = getSortSelectValue(currentSortKey, currentSortDirection);
    }
};

const formatListText = (values: string[] | undefined, emptyText: string): string => {
    if (!values || values.length === 0) {
        return emptyText;
    }

    return values.join(' / ');
};

const showDetailModal = async (reserve: ProgramReserve): Promise<void> => {
    let detail: Program;
    try {
        const response: Response = await fetch(`${API_ENDPOINTS.PROGRAM_DETAIL}?id=${reserve.programId}&kind=${RadioServiceKind[reserve.serviceKind]}`);

        if (!response.ok) {
            throw new Error('HTTP Error: ' + response.status);
        }

        const result = await response.json() as ApiResponseContract<Program>;
        detail = result.data;
    } catch (error) {
        const message = error instanceof Error ? error.message : `${error}`;
        showToast(message, false);
        return;
    }

    const template = document.getElementById('program-reserve-modal-template') as HTMLTemplateElement;

    const modal = template.content.cloneNode(true) as HTMLElement;

    setEventListener(modal, '.modal-background', 'click', () => document.getElementById('detail-modal')?.remove());
    setEventListener(modal, '.modal-card .modal-card-head button.delete', 'click', () => document.getElementById('detail-modal')?.remove());
    setEventListener(modal, 'footer.modal-card-foot .buttons button', 'click', () => document.getElementById('detail-modal')?.remove());

    setTextContent(modal, '.modal-program-title', detail.title);
    setTextContent(modal, '.modal-onair-date', `${new Date(detail.startTime).toLocaleString()} ～ ${new Date(detail.endTime).toLocaleString()}`);
    setTextContent(modal, '.modal-onair-station', detail.stationName);
    setTextContent(modal, '.modal-performer', detail.performer);
    setTextContent(
        modal,
        '.modal-keyword-reserves',
        formatListText(
            reserve.matchedKeywordReserveKeywords,
            reserve.reserveType === ReserveType.Keyword ? '（取得できませんでした）' : '（キーワード予約ではありません）'));
    setTextContent(modal, '.modal-planned-tags', formatListText(reserve.plannedTagNames, '（タグなし）'));
    setInnerHtml(modal, '.modal-description', sanitizeHtml(detail.description ?? ''));

    document.body.appendChild(modal);
};

