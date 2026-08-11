import {
    ApiResponseContract,
    NotificationEntryResponseContract as Notification,
    NotificationListResponseContract
} from './openapi-response-contract.js';
import { API_ENDPOINTS } from './const.js';
import { showGlobalToast } from './feedback.js';
import type { SignalRHubConnection, SignalRWindow } from './signalr-types.js';

const createTableCell = (content: string): HTMLTableCellElement => {
    const cell: HTMLTableCellElement = document.createElement('td');
    cell.textContent = content;
    return cell;
};

const createMobileItem = (notice: Notification): HTMLElement => {
    const item = document.createElement('article');
    item.className = 'rounded-xl border border-slate-200 bg-white p-3 shadow-sm';

    const date = document.createElement('p');
    date.className = 'text-xs text-zinc-500';
    date.textContent = new Date(notice.timestamp).toLocaleString();

    const message = document.createElement('p');
    message.className = 'mt-2 text-sm text-slate-800';
    message.textContent = notice.message;

    item.appendChild(date);
    item.appendChild(message);
    return item;
};

document.addEventListener('DOMContentLoaded', () => {

    let currentPage: number = 1;
    const pageSize: number = 10;
    let notificationHubConnection: SignalRHubConnection | null = null;
    let isRealtimeReloadRunning = false;
    let hasRealtimeReloadPending = false;



    const loadRecordings = async (page: number): Promise<void> => {

        const response: Response = await fetch(`${API_ENDPOINTS.NOTIFICATION_LIST}?page=${page}&pageSize=${pageSize}`);
        const result = await response.json() as ApiResponseContract<NotificationListResponseContract>;
        const data = result.data;
        renderRecordings(data.recordings);
        renderPagination(data.totalRecords, page, pageSize);
    };

    /**
     * SignalR経由の変更通知を受けた際に通知一覧を再同期する
     */
    const reloadNotificationsFromRealtimeAsync = async (): Promise<void> => {
        if (isRealtimeReloadRunning) {
            hasRealtimeReloadPending = true;
            return;
        }

        isRealtimeReloadRunning = true;
        try {
            do {
                hasRealtimeReloadPending = false;
                await loadRecordings(currentPage);
            } while (hasRealtimeReloadPending);
        } finally {
            isRealtimeReloadRunning = false;
        }
    };

    /**
     * お知らせ更新のSignalR接続を初期化する
     */
    const initializeNotificationHubConnectionAsync = async (): Promise<void> => {
        const signalRNamespace = (window as SignalRWindow).signalR;
        if (!signalRNamespace) {
            console.warn('SignalRクライアントが読み込まれていないため、お知らせPush同期を無効化します。');
            return;
        }

        const connection = new signalRNamespace.HubConnectionBuilder()
            .withUrl('/hubs/notifications')
            .withAutomaticReconnect()
            .configureLogging(signalRNamespace.LogLevel.Warning)
            .build();

        connection.on('notificationChanged', () => {
            void reloadNotificationsFromRealtimeAsync();
        });

        connection.onreconnected(() => {
            void reloadNotificationsFromRealtimeAsync();
        });

        connection.onclose((error) => {
            if (error) {
                console.warn('お知らせSignalR接続が切断されました。', error);
            }
        });

        try {
            await connection.start();
            notificationHubConnection = connection;
        } catch (error) {
            console.warn('お知らせSignalR接続の開始に失敗しました。', error);
        }
    };

    const renderRecordings = (recordings: Notification[]): void => {
        const tableBody: HTMLElement = document.getElementById('recordings-table-body') as HTMLElement;
        const mobileList: HTMLElement | null = document.getElementById('notifications-mobile-list');
        tableBody.innerHTML = '';
        if (mobileList) {
            mobileList.innerHTML = '';
        }

        recordings.forEach((notice: Notification) => {
            const row: HTMLTableRowElement = document.createElement('tr');

            const titleCell: HTMLTableCellElement = createTableCell(notice.message);
            const dateTimeCell: HTMLTableCellElement = createTableCell(new Date(notice.timestamp).toLocaleString());

            row.appendChild(dateTimeCell);
            row.appendChild(titleCell);

            tableBody.appendChild(row);

            if (mobileList) {
                mobileList.appendChild(createMobileItem(notice));
            }
        });
    };

    const renderPagination = (totalRecords: number, currentPage: number, pageSize: number): void => {
        const paginationList: HTMLElement = document.getElementById('pagination-list') as HTMLElement;
        paginationList.innerHTML = '';
        const totalPages: number = Math.ceil(totalRecords / pageSize);

        const maxPagesToShow: number = 11;
        let startPage: number = Math.max(1, currentPage - Math.floor(maxPagesToShow / 2));
        let endPage: number = Math.min(totalPages, startPage + maxPagesToShow - 1);

        if (endPage - startPage < maxPagesToShow - 1) {
            startPage = Math.max(1, endPage - maxPagesToShow + 1);
        }

        const prevPageButton: HTMLAnchorElement = document.createElement('a');
        prevPageButton.className = 'pagination-previous';
        prevPageButton.textContent = '<';
        prevPageButton.onclick = () => {
            if (currentPage > 1) {
                currentPage--;
                loadRecordings(currentPage);
            }
        };

        const prevPageItem: HTMLLIElement = document.createElement('li');
        prevPageItem.appendChild(prevPageButton);
        paginationList.appendChild(prevPageItem);

        if (startPage > 1) {
            const firstPageItem: HTMLLIElement = document.createElement('li');
            const firstPageLink: HTMLAnchorElement = document.createElement('a');
            firstPageLink.className = 'pagination-link';
            firstPageLink.textContent = '1';
            firstPageLink.addEventListener('click', () => {
                currentPage = 1;
                loadRecordings(currentPage);
            });
            firstPageItem.appendChild(firstPageLink);
            paginationList.appendChild(firstPageItem);

            if (startPage > 2) {
                const ellipsisItem: HTMLLIElement = document.createElement('li');
                const ellipsis: HTMLSpanElement = document.createElement('span');
                ellipsis.className = 'pagination-ellipsis';
                ellipsis.textContent = '...';
                ellipsisItem.appendChild(ellipsis);
                paginationList.appendChild(ellipsisItem);
            }
        }

        for (let i = startPage; i <= endPage; i++) {
            const listItem: HTMLLIElement = document.createElement('li');
            const link: HTMLAnchorElement = document.createElement('a');
            link.className = 'pagination-link';
            if (i === currentPage) {
                link.classList.add('is-current');
            }
            link.textContent = i.toString();
            link.addEventListener('click', () => {
                currentPage = i;
                loadRecordings(currentPage);
            });
            listItem.appendChild(link);
            paginationList.appendChild(listItem);
        }

        if (endPage < totalPages) {
            if (endPage < totalPages - 1) {
                const ellipsisItem: HTMLLIElement = document.createElement('li');
                const ellipsis: HTMLSpanElement = document.createElement('span');
                ellipsis.className = 'pagination-ellipsis';
                ellipsis.textContent = '...';
                ellipsisItem.appendChild(ellipsis);
                paginationList.appendChild(ellipsisItem);
            }

            const lastPageItem: HTMLLIElement = document.createElement('li');
            const lastPageLink: HTMLAnchorElement = document.createElement('a');
            lastPageLink.className = 'pagination-link';
            lastPageLink.textContent = totalPages.toString();
            lastPageLink.addEventListener('click', () => {
                currentPage = totalPages;
                loadRecordings(currentPage);
            });
            lastPageItem.appendChild(lastPageLink);
            paginationList.appendChild(lastPageItem);
        }

        const nextPageButton: HTMLAnchorElement = document.createElement('a');
        nextPageButton.className = 'pagination-next';
        nextPageButton.classList.toggle('is-disabled', currentPage === totalPages);
        nextPageButton.textContent = '>';
        nextPageButton.onclick = () => {
            if (currentPage < totalPages) {
                currentPage++;
                loadRecordings(currentPage);
            }
        };

        const nextPageItem: HTMLLIElement = document.createElement('li');
        nextPageItem.appendChild(nextPageButton);
        paginationList.appendChild(nextPageItem);
    };

    document.getElementById('clear-notification-button')?.addEventListener('click', async () => {
        const tableBody: HTMLElement = document.getElementById('recordings-table-body') as HTMLElement;
        tableBody.innerHTML = '';

        const paginationList: HTMLElement = document.getElementById('pagination-list') as HTMLElement;
        paginationList.innerHTML = '';

        await deleteNotification();

        currentPage = 1;
        await loadRecordings(currentPage);
    });

    window.addEventListener('beforeunload', () => {
        if (notificationHubConnection) {
            void notificationHubConnection.stop();
            notificationHubConnection = null;
        }
    });

    void loadRecordings(currentPage);
    void initializeNotificationHubConnectionAsync();
});


const deleteNotification = async (): Promise<void> => {

    try {
        await fetch(API_ENDPOINTS.NOTIFICATION_CLEAR, {
            method: 'POST'
        });
    }
    catch (error) {
        console.error('Error clearing notifications:', error);
        showGlobalToast('お知らせの削除に失敗しました。', false);
    }
}

