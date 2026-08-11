import {
    ApiResponseContract,
    DateElementResponseContract as DateElement,
    RadioProgramResponseContract as Program,
    StationDataResponseContract
} from './openapi-response-contract.js';
import { API_ENDPOINTS } from './const.js';
import { AvailabilityTimeFree, RecordingType, RadioServiceKind } from './define.js';
import type { ProgramInformationRequestContract } from './openapi-contract.js';
import { sanitizeHtml } from './utils.js';
import { createInlineToast, wireInlineToastClose } from './inline-toast.js';

const reservedRecordingKeys = new Set<string>();
const showProgramToast = createInlineToast('program-result-toast', 'program-result-toast-message');

async function reserveProgramWithToast(
    programId: string,
    recordingType: RecordingType,
    button: HTMLButtonElement): Promise<void> {
    const reservationKey = `${programId}:${recordingType}`;
    if (reservedRecordingKeys.has(reservationKey)) {
        return;
    }

    button.disabled = true;
    button.classList.add('opacity-70');

    try {
        const requestBody: ProgramInformationRequestContract = {
            programId: programId,
            radioServiceKind: RadioServiceKind.Radiko,
            recordingType: recordingType
        };

        const response = await fetch(API_ENDPOINTS.PROGRAM_RESERVE, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(requestBody)
        });

        const result = await response.json() as ApiResponseContract<null>;
        if (response.ok && result.success) {
            reservedRecordingKeys.add(reservationKey);
            button.textContent = '予約済み';
            button.classList.add('pointer-events-none');
            showProgramToast(result.message ?? '録音予約を開始しました。', true);
            return;
        }

        showProgramToast(result?.message ?? '録音予約に失敗しました。', false);
    } catch (error) {
        console.error('Error:', error);
        showProgramToast('録音予約に失敗しました。', false);
    }

    button.disabled = false;
    button.classList.remove('opacity-70');
}

document.addEventListener('DOMContentLoaded', async () => {
    wireInlineToastClose('program-result-toast-close', 'program-result-toast');

    const stationSelect = document.getElementById('stationSelect') as HTMLSelectElement;
    const dateSelect = document.getElementById('dateSelect') as HTMLSelectElement;

    await loadStationList();
    await populateDateSelect();

    stationSelect.addEventListener('change', async () => {

        const stationSelect = document.getElementById('stationSelect') as HTMLSelectElement;
        const stationId = stationSelect.value;
        const dateSelectContainer = document.getElementById('dateSelectContainer') as HTMLElement;
        const programList = document.getElementById('programList') as HTMLElement;

        if (stationId) {

            dateSelectContainer.classList.remove('hidden');

            await loadPrograms();

        } else {

            dateSelectContainer.classList.add('hidden');
            programList.innerHTML = '';

        }
    });
    dateSelect.addEventListener('change', loadPrograms);
});

async function loadStationList(): Promise<void> {
    const response = await fetch(API_ENDPOINTS.STATION_LIST_RADIKO);
    const result = await response.json() as ApiResponseContract<StationDataResponseContract>;
    const stationsByRegion = result.data;
    const stationSelect = document.getElementById('stationSelect') as HTMLSelectElement;

    for (const region in stationsByRegion) {
        const optGroup = document.createElement('optgroup');
        optGroup.label = region;
        stationSelect.appendChild(optGroup);

        stationsByRegion[region].forEach((station: { stationId: string; stationName: string; }) => {
            const option = document.createElement('option');
            option.value = station.stationId;
            option.textContent = station.stationName;
            optGroup.appendChild(option);
        });
    }
}

async function populateDateSelect(): Promise<void> {
    const response = await fetch(API_ENDPOINTS.RADIO_DATE);
    const result = await response.json() as ApiResponseContract<DateElement[]>;
    const dates = result.data ?? [];
    const dateSelect = document.getElementById('dateSelect') as HTMLSelectElement;

    dateSelect.replaceChildren();

    dates.forEach(dateElm => {
        const option = document.createElement('option');
        option.value = dateElm.value;
        option.textContent = dateElm.textContent;
        if (dateElm.isToday) {
            option.selected = true;
        }
        dateSelect.appendChild(option);
    });
}

async function loadPrograms(): Promise<void> {
    const stationSelect = document.getElementById('stationSelect') as HTMLSelectElement;
    const dateSelect = document.getElementById('dateSelect') as HTMLSelectElement;
    const stationId = stationSelect.value;
    const date = dateSelect.value;

    if (stationId && date) {
        const response = await fetch(`${API_ENDPOINTS.PROGRAM_LIST_RADIKO}?d=${date}&s=${stationId}`);
        const result = await response.json() as ApiResponseContract<Program[]>;
        const programs = result.data ?? [];
        renderPrograms(programs);
    } else {
        const programList = document.getElementById('programList') as HTMLElement;
        programList.replaceChildren();
    }
}

function renderPrograms(programs: Program[]): void {
    const programList = document.getElementById('programList') as HTMLElement;
    const emptyState = document.getElementById('programs-empty') as HTMLElement | null;
    const template = document.getElementById('program-card-template') as HTMLTemplateElement | null;

    programList.replaceChildren();
    if (!template) {
        return;
    }

    const sortedPrograms = [...programs].sort(
        (a, b) => new Date(a.startTime).getTime() - new Date(b.startTime).getTime());

    if (sortedPrograms.length === 0) {
        emptyState?.classList.remove('hidden');
        return;
    }

    emptyState?.classList.add('hidden');

    sortedPrograms.forEach(program => {
        const card = template.content.cloneNode(true) as HTMLElement;

        const startTime = new Date(program.startTime);
        const endTime = new Date(program.endTime);
        const now = new Date();

        const title = card.querySelector('.title') as HTMLElement | null;
        const statusBadge = card.querySelector('.status-badge') as HTMLElement | null;
        const subtitle = card.querySelector('.subtitle') as HTMLElement | null;
        const performer = card.querySelector('.performer') as HTMLElement | null;
        const start = card.querySelector('.startTime') as HTMLElement | null;
        const end = card.querySelector('.endTime') as HTMLElement | null;
        const description = card.querySelector('.description') as HTMLElement | null;
        const descriptionToggle = card.querySelector('.description-toggle') as HTMLButtonElement | null;
        const realtimeBtn = card.querySelector('.record-btn') as HTMLButtonElement | null;
        const timeFreeBtn = card.querySelector('.timefree-btn') as HTMLButtonElement | null;

        if (title) {
            title.textContent = program.title;
        }

        if (statusBadge) {
            statusBadge.classList.add('hidden');
            statusBadge.classList.remove('is-live', 'is-ended');
            if (startTime <= now && now < endTime) {
                statusBadge.textContent = 'Now';
                statusBadge.classList.add('is-live');
                statusBadge.classList.remove('hidden');
            } else if (endTime <= now) {
                statusBadge.textContent = 'Ended';
                statusBadge.classList.add('is-ended');
                statusBadge.classList.remove('hidden');
            }
        }

        if (subtitle) {
            subtitle.textContent = `${program.stationName} (${program.stationId})`;
        }

        if (performer) {
            performer.textContent = program.performer ? `出演: ${program.performer}` : '';
        }

        if (start) {
            start.textContent = `開始時間: ${startTime.toLocaleString()}`;
        }

        if (end) {
            end.textContent = `終了時間: ${endTime.toLocaleString()}`;
        }

        const descriptionText = program.description ?? '';
        if (description) {
            description.innerHTML = sanitizeHtml(descriptionText);
        }

        if (description && descriptionToggle) {
            const plainText = descriptionText.replace(/<[^>]*>/g, '').trim();
            if (plainText.length > 140) {
                description.classList.add('is-collapsed');
                descriptionToggle.classList.remove('hidden');
                descriptionToggle.addEventListener('click', () => {
                    const isCollapsed = description.classList.contains('is-collapsed');
                    if (isCollapsed) {
                        description.classList.remove('is-collapsed');
                        descriptionToggle.textContent = 'less';
                    } else {
                        description.classList.add('is-collapsed');
                        descriptionToggle.textContent = 'more';
                    }
                });
            } else {
                descriptionToggle.classList.add('hidden');
            }
        }

        const isTimeFreeAvailable =
            program.availabilityTimeFree === AvailabilityTimeFree.Available ||
            program.availabilityTimeFree === AvailabilityTimeFree.PartiallyAvailable;

        if (timeFreeBtn) {
            if (isTimeFreeAvailable) {
                timeFreeBtn.onclick = () => reserveProgramWithToast(program.programId, RecordingType.TimeFree, timeFreeBtn);
            } else {
                timeFreeBtn.classList.add('hidden');
            }
        }

        if (realtimeBtn) {
            if (now < endTime) {
                realtimeBtn.onclick = () => reserveProgramWithToast(program.programId, RecordingType.RealTime, realtimeBtn);
            } else {
                realtimeBtn.classList.add('hidden');
            }
        }

        programList.appendChild(card);
    });
}

