import { API_ENDPOINTS } from './const.js'
import type { ProgramInformationRequestContract } from './openapi-contract.js';
import type { ApiResponseContract } from './openapi-response-contract.js';

/**
* 折り畳み可能なCardの生成
* @param {string} cardId カード固有のID
* @param {HTMLElement} titleElement ヘッダー部に表示するタイトル要素
* @param {HTMLElement[]} columnsElements コンテンツ部に表示するカラム要素
* @returns {HTMLElement} Card要素
*/
export function createCard(cardId: string, titleElement: HTMLElement, contentElements: HTMLElement[]): HTMLElement {

    // カード全体を生成
    const card: HTMLDivElement = document.createElement('div');
    card.className = 'card';

    // ヘッダー部分を生成
    const cardHeader: HTMLHeadElement = document.createElement('header');
    cardHeader.className = 'card-header';
    cardHeader.addEventListener('click', () => {
        const content = document.getElementById(cardId + '_content');
        if (content) {
            content.classList.toggle('hidden');
            const icon = cardHeaderIcon.querySelector('i');
            if (icon) {
                icon.classList.toggle('fa-angle-down');
                icon.classList.toggle('fa-angle-up');
            }
        }
    });
    card.appendChild(cardHeader);

    // ヘッダーのタイトル部分を生成
    const cardHeaderTitle: HTMLParagraphElement = document.createElement('p');
    cardHeaderTitle.className = 'card-header-title';
    cardHeaderTitle.appendChild(titleElement);
    cardHeader.appendChild(cardHeaderTitle);

    // ヘッダーのボタン部分を生成
    const cardHeaderIcon: HTMLButtonElement = document.createElement('button');
    cardHeaderIcon.className = 'card-header-icon';
    cardHeader.appendChild(cardHeaderIcon);

    // アイコン部分を生成
    const iconSpan: HTMLSpanElement = document.createElement('span');
    iconSpan.className = 'icon';
    cardHeaderIcon.appendChild(iconSpan);

    const icon: HTMLElement = document.createElement('i');
    icon.className = 'fas fa-angle-down';
    icon.setAttribute('aria-hidden', 'true');
    iconSpan.appendChild(icon);

    // カードのコンテンツ部分を生成
    const cardContent: HTMLDivElement = document.createElement('div');
    cardContent.id = cardId + '_content';
    cardContent.className = 'card-content hidden';
    card.appendChild(cardContent);

    // コンテンツの内部を生成
    const content: HTMLDivElement = document.createElement('div');
    content.className = 'content';
    cardContent.appendChild(content);

    // 引数で受け取ったコンテンツ用カラムを生成
    contentElements.forEach(element => content.appendChild(element));

    return card;
}



export function createColumns(label: string, value: string): HTMLDivElement {
    const columns = document.createElement('div');
    columns.className = 'columns';

    const labelColumn = document.createElement('div');
    labelColumn.className = 'column is-one-quarter';
    labelColumn.innerHTML = `<strong>${label}</strong>`;

    const valueColumn = document.createElement('div');
    valueColumn.className = 'column';
    valueColumn.innerHTML = value;

    columns.appendChild(labelColumn);
    columns.appendChild(valueColumn);

    return columns;
}



export async function reserveProgram(programId: string, serviceKind: number, type: number): Promise<void> {
    try {
        const requestBody: ProgramInformationRequestContract = {
            programId: programId,
            radioServiceKind: serviceKind,
            recordingType: type
        };

        const response = await fetch(API_ENDPOINTS.PROGRAM_RESERVE, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(requestBody)
        });

        const result = await response.json() as ApiResponseContract<null>;
        if (result.success) {
            showModal(result.message ?? '録音予約を開始しました');
        } else {
            showModal(result.message ?? '録音予約に失敗しました');
        }
    } catch (error) {
        console.error('Error:', error);
        showModal('録音予約に失敗しました');
    }
}



export function showModal(message: string): void {
    const modalMessage = document.getElementById('modalMessage') as HTMLElement;
    modalMessage.textContent = message;
    const modal = document.getElementById('recordingModal') as HTMLElement;
    modal.classList.add('is-active');
    setTimeout(closeModal, 5000);
}

export function closeModal(): void {
    const modal = document.getElementById('recordingModal') as HTMLElement;
    modal.classList.remove('is-active');
}

