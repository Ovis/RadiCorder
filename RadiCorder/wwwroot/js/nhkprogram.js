import { API_ENDPOINTS } from './const.js';
import { RecordingType, RadioServiceKind } from './define.js';
import { parseUtcDateTime, sanitizeHtml } from './utils.js';
import { createInlineToast, wireInlineToastClose } from './inline-toast.js';
const reservedRecordingKeys = new Set();
const showProgramToast = createInlineToast('program-result-toast', 'program-result-toast-message');
async function reserveProgramWithToast(programId, recordingType, button) {
    const reservationKey = `${programId}:${recordingType}`;
    if (reservedRecordingKeys.has(reservationKey)) {
        return;
    }
    button.disabled = true;
    button.classList.add('opacity-70');
    try {
        const requestBody = {
            programId: programId,
            radioServiceKind: RadioServiceKind.Radiru,
            recordingType: recordingType
        };
        const response = await fetch(API_ENDPOINTS.PROGRAM_RESERVE, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(requestBody)
        });
        const result = await response.json();
        if (response.ok && result.success) {
            reservedRecordingKeys.add(reservationKey);
            button.textContent = '予約済み';
            button.classList.add('pointer-events-none');
            showProgramToast(result.message ?? '録音予約を開始しました。', true);
            return;
        }
        showProgramToast(result?.message ?? '録音予約に失敗しました。', false);
    }
    catch (error) {
        console.error('Error:', error);
        showProgramToast('録音予約に失敗しました。', false);
    }
    button.disabled = false;
    button.classList.remove('opacity-70');
}
document.addEventListener('DOMContentLoaded', async () => {
    wireInlineToastClose('program-result-toast-close', 'program-result-toast');
    const stationSelect = document.getElementById('stationSelect');
    const dateSelect = document.getElementById('dateSelect');
    await loadStationList();
    await populateDateSelect();
    stationSelect.addEventListener('change', async () => {
        const stationSelect = document.getElementById('stationSelect');
        const stationId = stationSelect.value;
        const dateSelectContainer = document.getElementById('dateSelectContainer');
        const programList = document.getElementById('programList');
        if (stationId) {
            dateSelectContainer.classList.remove('hidden');
            await loadPrograms();
        }
        else {
            dateSelectContainer.classList.add('hidden');
            programList.innerHTML = '';
        }
    });
    dateSelect.addEventListener('change', loadPrograms);
});
async function loadStationList() {
    const response = await fetch(API_ENDPOINTS.STATION_LIST_RADIRU);
    const result = await response.json();
    const stationsByRegion = result.data;
    const stationSelect = document.getElementById('stationSelect');
    for (const region in stationsByRegion) {
        const optGroup = document.createElement('optgroup');
        optGroup.label = region;
        stationSelect.appendChild(optGroup);
        stationsByRegion[region].forEach((station) => {
            const option = document.createElement('option');
            option.value = station.stationId;
            option.textContent = station.stationName;
            option.dataset.region = station.areaId;
            optGroup.appendChild(option);
        });
    }
}
async function populateDateSelect() {
    const response = await fetch(API_ENDPOINTS.RADIO_DATE);
    const result = await response.json();
    const dates = result.data ?? [];
    const dateSelect = document.getElementById('dateSelect');
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
async function loadPrograms() {
    const stationSelect = document.getElementById('stationSelect');
    const dateSelect = document.getElementById('dateSelect');
    const stationId = stationSelect.value;
    const areaId = stationSelect[stationSelect.selectedIndex].dataset.region;
    const date = dateSelect.value;
    if (stationId && date) {
        const response = await fetch(`${API_ENDPOINTS.PROGRAM_LIST_RADIRU}?d=${date}&s=${stationId}&a=${areaId}`);
        const result = await response.json();
        const programs = result.data ?? [];
        renderPrograms(programs);
    }
    else {
        const programList = document.getElementById('programList');
        programList.replaceChildren();
    }
}
function renderPrograms(programs) {
    const programList = document.getElementById('programList');
    const emptyState = document.getElementById('programs-empty');
    const template = document.getElementById('program-card-template');
    programList.replaceChildren();
    if (!template) {
        return;
    }
    const sortedPrograms = [...programs].sort((a, b) => new Date(a.startTime).getTime() - new Date(b.startTime).getTime());
    if (sortedPrograms.length === 0) {
        emptyState?.classList.remove('hidden');
        return;
    }
    emptyState?.classList.add('hidden');
    sortedPrograms.forEach(program => {
        const card = template.content.cloneNode(true);
        const startTime = new Date(program.startTime);
        const endTime = new Date(program.endTime);
        const now = new Date();
        const title = card.querySelector('.title');
        const statusBadge = card.querySelector('.status-badge');
        const subtitle = card.querySelector('.subtitle');
        const performer = card.querySelector('.performer');
        const start = card.querySelector('.startTime');
        const end = card.querySelector('.endTime');
        const description = card.querySelector('.description');
        const descriptionToggle = card.querySelector('.description-toggle');
        const realtimeBtn = card.querySelector('.record-btn');
        const onDemandBtn = card.querySelector('.ondemand-btn');
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
            }
            else if (endTime <= now) {
                statusBadge.textContent = 'Ended';
                statusBadge.classList.add('is-ended');
                statusBadge.classList.remove('hidden');
            }
        }
        if (subtitle) {
            subtitle.textContent = `${program.stationName} (${program.areaName})`;
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
                    }
                    else {
                        description.classList.add('is-collapsed');
                        descriptionToggle.textContent = 'more';
                    }
                });
            }
            else {
                descriptionToggle.classList.add('hidden');
            }
        }
        if (realtimeBtn) {
            if (now < endTime) {
                realtimeBtn.onclick = () => reserveProgramWithToast(program.programId, RecordingType.RealTime, realtimeBtn);
            }
            else {
                realtimeBtn.classList.add('hidden');
            }
        }
        if (onDemandBtn) {
            const expires = parseUtcDateTime(program.onDemandExpiresAtUtc);
            const isExpired = !expires || Number.isNaN(expires.getTime()) || expires.getTime() <= now.getTime();
            const canShow = endTime <= now;
            const hasOnDemandUrl = !!program.onDemandContentUrl;
            if (canShow) {
                onDemandBtn.classList.remove('hidden');
                if (hasOnDemandUrl && !isExpired) {
                    onDemandBtn.disabled = false;
                    onDemandBtn.classList.remove('opacity-70', 'pointer-events-none');
                    onDemandBtn.textContent = '聞き逃し配信録音';
                    onDemandBtn.onclick = () => reserveProgramWithToast(program.programId, RecordingType.OnDemand, onDemandBtn);
                }
                else {
                    onDemandBtn.disabled = true;
                    onDemandBtn.classList.add('opacity-70', 'pointer-events-none');
                    onDemandBtn.textContent = '聞き逃し期限切れ';
                }
            }
            else {
                onDemandBtn.classList.add('hidden');
            }
        }
        programList.appendChild(card);
    });
}
//# sourceMappingURL=nhkprogram.js.map