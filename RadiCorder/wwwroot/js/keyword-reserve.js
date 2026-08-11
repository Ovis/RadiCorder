import { API_ENDPOINTS, getDayOfWeekShortString } from './const.js';
import { showConfirmDialog, showGlobalToast } from './feedback.js';
import { setTextContent, setAttribute, setEventListener } from './utils.js';
let availableTags = [];
let keywordReserves = [];
let draggingReserveId = null;
let isReordering = false;
const normalizeKeywordReserve = (entry) => ({
    id: String(entry.id ?? ''),
    sortOrder: Number(entry.sortOrder ?? 0),
    selectedRadikoStationIds: entry.selectedRadikoStationIds ?? [],
    selectedRadiruStationIds: entry.selectedRadiruStationIds ?? [],
    keyword: entry.keyword ?? '',
    searchTitleOnly: entry.searchTitleOnly ?? false,
    excludedKeyword: entry.excludedKeyword ?? '',
    excludeTitleOnly: entry.excludeTitleOnly ?? false,
    recordPath: entry.recordPath ?? '',
    recordFileName: entry.recordFileName ?? '',
    selectedDaysOfWeek: (entry.selectedDaysOfWeek ?? []).map((x) => Number(x)),
    startTime: (entry.startTime ?? entry.startTimeString ?? '00:00:00'),
    endTime: (entry.endTime ?? entry.endTimeString ?? '00:00:00'),
    isEnabled: entry.isEnabled ?? false,
    startDelay: entry.startDelay === null || entry.startDelay === undefined ? undefined : Number(entry.startDelay),
    endDelay: entry.endDelay === null || entry.endDelay === undefined ? undefined : Number(entry.endDelay),
    tagIds: entry.tagIds ?? [],
    tags: entry.tags ?? [],
    mergeTagBehavior: entry.mergeTagBehavior === undefined ? undefined : Number(entry.mergeTagBehavior)
});
const normalizeTagName = (value) => value.trim().toLocaleLowerCase();
document.addEventListener('DOMContentLoaded', () => {
    loadTags().then(() => loadRecordings());
});
const loadTags = async () => {
    try {
        const response = await fetch(API_ENDPOINTS.TAGS);
        const result = await response.json();
        availableTags = result.data ?? [];
    }
    catch (error) {
        console.error('Error loading tags:', error);
        availableTags = [];
    }
};
const loadRecordings = async () => {
    try {
        const response = await fetch(API_ENDPOINTS.RESERVE_KEYWORD_LIST);
        const result = await response.json();
        const data = (result.data ?? [])
            .map(normalizeKeywordReserve)
            .slice()
            .sort((a, b) => (a.sortOrder ?? Number.MAX_SAFE_INTEGER) - (b.sortOrder ?? Number.MAX_SAFE_INTEGER));
        keywordReserves = data;
        renderRecordings(keywordReserves);
    }
    catch (error) {
        console.error('Error loading recordings:', error);
    }
};
const persistKeywordReserveOrder = async () => {
    if (isReordering) {
        return false;
    }
    isReordering = true;
    try {
        const ids = keywordReserves.map((reserve) => reserve.id);
        const requestBody = { ids };
        const response = await fetch(API_ENDPOINTS.RESERVE_KEYWORD_REORDER, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(requestBody)
        });
        const result = await response.json();
        if (!result.success) {
            showGlobalToast(result.message ?? '並び順の更新に失敗しました。', false);
            await loadRecordings();
            return false;
        }
        keywordReserves = keywordReserves.map((x, index) => ({ ...x, sortOrder: index }));
        showGlobalToast(result.message ?? '並び順を更新しました。');
        renderRecordings(keywordReserves);
        return true;
    }
    catch (error) {
        console.error('Error:', error);
        showGlobalToast('並び順の更新に失敗しました。', false);
        await loadRecordings();
        return false;
    }
    finally {
        isReordering = false;
    }
};
const moveReserve = async (reserveId, direction) => {
    const currentIndex = keywordReserves.findIndex((reserve) => reserve.id === reserveId);
    if (currentIndex < 0) {
        return;
    }
    const targetIndex = direction === 'up' ? currentIndex - 1 : currentIndex + 1;
    if (targetIndex < 0 || targetIndex >= keywordReserves.length) {
        return;
    }
    const reordered = [...keywordReserves];
    const [moved] = reordered.splice(currentIndex, 1);
    reordered.splice(targetIndex, 0, moved);
    keywordReserves = reordered;
    renderRecordings(keywordReserves);
    await persistKeywordReserveOrder();
};
const clearMultiSelect = (selectElm) => {
    Array.from(selectElm.options).forEach((option) => {
        option.selected = false;
    });
};
const renderSelectedTagChips = (selectElm, container) => {
    if (!selectElm || !container) {
        return;
    }
    container.innerHTML = '';
    const selectedOptions = Array.from(selectElm.selectedOptions);
    if (selectedOptions.length === 0) {
        const empty = document.createElement('span');
        empty.className = 'keyword-selected-tags-empty';
        empty.textContent = '未選択';
        container.appendChild(empty);
        return;
    }
    selectedOptions.forEach((option) => {
        const chip = document.createElement('span');
        chip.className = 'keyword-selected-tag-chip';
        const text = document.createElement('span');
        text.textContent = option.textContent ?? option.value;
        const removeButton = document.createElement('button');
        removeButton.type = 'button';
        removeButton.setAttribute('aria-label', `${option.textContent ?? option.value} を解除`);
        removeButton.textContent = '×';
        removeButton.addEventListener('click', () => {
            option.selected = false;
            selectElm.dispatchEvent(new Event('change'));
        });
        chip.appendChild(text);
        chip.appendChild(removeButton);
        container.appendChild(chip);
    });
};
const renderRecordings = async (recordings) => {
    const tableBody = document.getElementById('keyword-reserve-table-body');
    tableBody.innerHTML = '';
    const template = document.getElementById('keyword-reserve-table-body-template');
    recordings.forEach((reserve) => {
        const row = template.content.cloneNode(true);
        setAttribute(row, "tr", 'id', reserve.id);
        setTextContent(row, ".reserve-keyword", `${reserve.keyword}`);
        setTextContent(row, ".reserve-time-span", `${reserve.startTime.substring(0, 5)}～${reserve.endTime.substring(0, 5)}`);
        setTextContent(row, ".target-day-of-week", getDayOfWeekShortString(reserve.selectedDaysOfWeek));
        setTextContent(row, ".reserve-status", reserve.isEnabled ? '有効' : '無効');
        setTextContent(row, ".reserve-order-number", `${(reserve.sortOrder ?? recordings.indexOf(reserve)) + 1}`);
        setEventListener(row, "a.detail-button", "click", () => showKeywordReserveModal(reserve.id));
        setEventListener(row, ".move-up-button", "click", async () => await moveReserve(reserve.id, 'up'));
        setEventListener(row, ".move-down-button", "click", async () => await moveReserve(reserve.id, 'down'));
        const deleteAction = async () => {
            const data = {
                id: reserve.id,
            };
            const confirmed = await showConfirmDialog('削除してもよいですか？', { okText: '削除する' });
            if (!confirmed) {
                return;
            }
            try {
                const response = await fetch(API_ENDPOINTS.DELETE_KEYWORD_RESERVE, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify(data)
                });
                const result = await response.json();
                if (result.success) {
                    keywordReserves = keywordReserves.filter((x) => x.id !== reserve.id);
                    renderRecordings(keywordReserves);
                    showGlobalToast(result.message ?? "削除しました。");
                }
                else {
                    showGlobalToast(result.message ?? "削除に失敗しました。", false);
                }
            }
            catch (error) {
                console.error('Error:', error);
                showGlobalToast('エラーが発生しました。', false);
            }
        };
        setEventListener(row, "a.delete-button", "click", async () => await deleteAction());
        setAttribute(row, "tr", 'data-json', JSON.stringify(reserve));
        const rowElement = row.querySelector('tr');
        const dragHandleElement = row.querySelector('.reserve-order-handle');
        if (rowElement) {
            dragHandleElement?.addEventListener('dragstart', (event) => {
                draggingReserveId = reserve.id;
                event.dataTransfer?.setData('text/plain', reserve.id);
                rowElement.classList.add('is-dragging');
            });
            dragHandleElement?.addEventListener('dragend', () => {
                draggingReserveId = null;
                rowElement.classList.remove('is-dragging');
                document.querySelectorAll('#keyword-reserve-table-body tr')
                    .forEach((tr) => tr.classList.remove('is-drag-over'));
            });
            rowElement.addEventListener('dragover', (event) => {
                event.preventDefault();
                rowElement.classList.add('is-drag-over');
            });
            rowElement.addEventListener('dragleave', () => {
                rowElement.classList.remove('is-drag-over');
            });
            rowElement.addEventListener('drop', async (event) => {
                event.preventDefault();
                rowElement.classList.remove('is-drag-over');
                const droppedId = draggingReserveId ?? event.dataTransfer?.getData('text/plain');
                if (!droppedId || droppedId === reserve.id) {
                    return;
                }
                const fromIndex = keywordReserves.findIndex((x) => x.id === droppedId);
                const toIndex = keywordReserves.findIndex((x) => x.id === reserve.id);
                if (fromIndex < 0 || toIndex < 0) {
                    return;
                }
                const reordered = [...keywordReserves];
                const [moved] = reordered.splice(fromIndex, 1);
                reordered.splice(toIndex, 0, moved);
                keywordReserves = reordered;
                renderRecordings(keywordReserves);
                await persistKeywordReserveOrder();
            });
        }
        tableBody.appendChild(row);
    });
};
async function showKeywordReserveModal(rowId) {
    const trElm = document.getElementById(rowId);
    const json = trElm.dataset.json;
    if (!json) {
        console.error('JSON data is missing on the table row element.');
        return;
    }
    const reserveEntry = normalizeKeywordReserve(JSON.parse(json));
    const template = document.getElementById('keyword-reserve-modal-template');
    const modal = template.content.cloneNode(true);
    setEventListener(modal, ".modal-card .modal-card-head button.delete", "click", () => document.getElementById('keyword-reserve-modal')?.remove());
    setEventListener(modal, "#cancel-button", "click", () => document.getElementById('keyword-reserve-modal')?.remove());
    reserveEntry.selectedRadikoStationIds.forEach((stationId) => {
        const checkbox = modal.querySelector(`input[data-station="${stationId}"]`);
        if (checkbox) {
            checkbox.checked = true;
        }
    });
    const radikoStationCard = modal.querySelector('#radiko-card-header');
    (radikoStationCard.querySelector('.card-header'))?.addEventListener('click', () => {
        const content = radikoStationCard.querySelector('.card-content');
        if (content) {
            content.classList.toggle('hidden');
            const icon = radikoStationCard.querySelector('.card-header-icon i');
            if (icon) {
                icon.classList.toggle('fa-angle-down');
                icon.classList.toggle('fa-angle-up');
            }
        }
    });
    reserveEntry.selectedRadiruStationIds.forEach((stationId) => {
        const checkbox = modal.querySelector(`input[data-station="${stationId}"]`);
        if (checkbox) {
            checkbox.checked = true;
        }
    });
    const radiruStationCard = modal.querySelector('#radiru-card-header');
    radiruStationCard.querySelector('.card-header')?.addEventListener('click', () => {
        const content = radiruStationCard.querySelector('.card-content');
        if (content) {
            content.classList.toggle('hidden');
            const icon = radiruStationCard.querySelector('.card-header-icon i');
            if (icon) {
                icon.classList.toggle('fa-angle-down');
                icon.classList.toggle('fa-angle-up');
            }
        }
    });
    modal.querySelectorAll('.region').forEach((region) => {
        const regionCheckbox = region.querySelector('.region-checkbox-all');
        const stationCheckboxes = region.querySelectorAll('input.region-checkbox');
        const allChecked = Array.from(stationCheckboxes).every(checkbox => checkbox.checked);
        if (allChecked) {
            regionCheckbox.checked = true;
        }
    });
    const tagSelect = modal.querySelector('#keywordTagIds');
    const tagChipsContainer = modal.querySelector('#keywordTagIdsChips');
    const tagClearButton = modal.querySelector('#keywordTagIdsClear');
    const tagCreateInput = modal.querySelector('#keywordTagCreateName');
    const tagCreateButton = modal.querySelector('#keywordTagCreateButton');
    const tagCreateSuggestions = modal.querySelector('#keywordTagCreateSuggestions');
    if (tagSelect) {
        const rebuildTagOptions = (selectedIds) => {
            tagSelect.innerHTML = '';
            availableTags.forEach((tag) => {
                const option = document.createElement('option');
                option.value = tag.id;
                option.textContent = tag.name;
                option.selected = selectedIds.has(tag.id);
                tagSelect.appendChild(option);
            });
        };
        const selectTagById = (tagId) => {
            Array.from(tagSelect.options).forEach((option) => {
                if (option.value === tagId) {
                    option.selected = true;
                }
            });
            tagSelect.dispatchEvent(new Event('change'));
        };
        const renderTagCreateSuggestions = () => {
            if (!tagCreateInput || !tagCreateSuggestions) {
                return;
            }
            tagCreateSuggestions.innerHTML = '';
            const keyword = tagCreateInput.value.trim();
            if (!keyword) {
                return;
            }
            const normalizedKeyword = normalizeTagName(keyword);
            const exact = availableTags.find((tag) => normalizeTagName(tag.name) === normalizedKeyword);
            if (!exact) {
                const createButton = document.createElement('button');
                createButton.type = 'button';
                createButton.className = 'button is-small is-info is-light';
                createButton.textContent = `「${keyword}」を新規作成`;
                createButton.addEventListener('click', () => {
                    tagCreateButton?.click();
                });
                tagCreateSuggestions.appendChild(createButton);
            }
            availableTags
                .filter((tag) => tag.name.includes(keyword))
                .slice(0, 8)
                .forEach((tag) => {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'button is-small is-light';
                button.textContent = tag.name;
                button.addEventListener('click', () => {
                    selectTagById(tag.id);
                    if (tagCreateInput) {
                        tagCreateInput.value = '';
                    }
                    tagCreateSuggestions.innerHTML = '';
                    showGlobalToast(`タグ「${tag.name}」を選択しました。`);
                });
                tagCreateSuggestions.appendChild(button);
            });
        };
        rebuildTagOptions(new Set(reserveEntry.tagIds ?? []));
        tagSelect.addEventListener('change', () => {
            renderSelectedTagChips(tagSelect, tagChipsContainer);
        });
        if (tagClearButton) {
            tagClearButton.addEventListener('click', () => {
                clearMultiSelect(tagSelect);
                renderSelectedTagChips(tagSelect, tagChipsContainer);
            });
        }
        renderSelectedTagChips(tagSelect, tagChipsContainer);
        if (tagCreateInput) {
            tagCreateInput.addEventListener('input', renderTagCreateSuggestions);
            tagCreateInput.addEventListener('keydown', (event) => {
                if (event.key === 'Enter') {
                    event.preventDefault();
                }
            });
        }
        if (tagCreateButton && tagCreateInput) {
            tagCreateButton.addEventListener('click', async () => {
                const name = tagCreateInput.value.trim();
                if (!name) {
                    showGlobalToast('タグ名を入力してください。', false);
                    return;
                }
                const normalized = normalizeTagName(name);
                const existing = availableTags.find((tag) => normalizeTagName(tag.name) === normalized);
                if (existing) {
                    selectTagById(existing.id);
                    tagCreateInput.value = '';
                    renderTagCreateSuggestions();
                    showGlobalToast(`タグ「${existing.name}」を選択しました。`);
                    return;
                }
                const confirmed = await showConfirmDialog(`タグ「${name}」を作成しますか？`, { okText: '作成する' });
                if (!confirmed) {
                    return;
                }
                try {
                    const selectedIds = new Set(Array.from(tagSelect.selectedOptions).map(option => option.value));
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
                    if (createdTagId) {
                        selectedIds.add(createdTagId);
                    }
                    await loadTags();
                    rebuildTagOptions(selectedIds);
                    renderSelectedTagChips(tagSelect, tagChipsContainer);
                    tagCreateInput.value = '';
                    renderTagCreateSuggestions();
                    showGlobalToast(result.message ?? 'タグを作成しました。');
                }
                catch (error) {
                    console.error('Error:', error);
                    showGlobalToast('タグ作成に失敗しました。', false);
                }
            });
        }
    }
    setAttribute(modal, "#keyword", 'value', reserveEntry.keyword);
    if (reserveEntry.searchTitleOnly) {
        const searchTitleOnly = modal.querySelector('#searchTitleOnly');
        searchTitleOnly.checked = true;
    }
    setAttribute(modal, "#excludedKeyword", 'value', reserveEntry.excludedKeyword);
    if (reserveEntry.excludeTitleOnly) {
        const excludeTitleOnly = modal.querySelector('#excludeTitleOnly');
        excludeTitleOnly.checked = true;
    }
    const dayOfWeekInputs = modal.querySelectorAll('input[name="selectedDaysOfWeek"]');
    dayOfWeekInputs.forEach((input) => {
        if (reserveEntry.selectedDaysOfWeek.includes(Number(input.value))) {
            input.checked = true;
        }
    });
    setAttribute(modal, "#startTime", "value", reserveEntry.startTime.substring(0, 5));
    setAttribute(modal, "#endTime", "value", reserveEntry.endTime.substring(0, 5));
    setAttribute(modal, "#recordPath", "value", reserveEntry.recordPath);
    setAttribute(modal, "#recordFileName", "value", reserveEntry.recordFileName);
    setAttribute(modal, "#startDelay", "value", reserveEntry.startDelay?.toString() ?? '');
    setAttribute(modal, "#endDelay", "value", reserveEntry.endDelay?.toString() ?? '');
    const mergeTagBehaviorSelect = modal.querySelector('#mergeTagBehavior');
    if (mergeTagBehaviorSelect) {
        mergeTagBehaviorSelect.value = String(reserveEntry.mergeTagBehavior ?? 0);
    }
    if (reserveEntry.isEnabled) {
        const isEnabledElm = modal.querySelector('#enabled');
        isEnabledElm.checked = true;
    }
    const updateButton = modal.querySelector('#update-button');
    updateButton.addEventListener('click', async () => {
        const selectedRadikoStationIds = Array.from(document.querySelectorAll('input[name="SelectedRadikoStationIds"]:checked')).map(checkbox => checkbox.value);
        const selectedRadiruStationIds = Array.from(document.querySelectorAll('input[name="SelectedRadiruStationIds"]:checked')).map(checkbox => checkbox.value);
        const selectedDaysOfWeek = Array.from(document.querySelectorAll('input[name="selectedDaysOfWeek"]:checked')).map(checkbox => Number(checkbox.value));
        const selectedTagIds = Array.from(document.querySelectorAll('#keywordTagIds option:checked')).map(option => option.value);
        const keyword = document.getElementById('keyword').value;
        const searchTitleOnly = document.getElementById('searchTitleOnly').checked;
        const excludedKeyword = document.getElementById('excludedKeyword').value;
        const excludeTitleOnly = document.getElementById('excludeTitleOnly').checked;
        const startTime = document.getElementById('startTime').value;
        const endTime = document.getElementById('endTime').value;
        const recordPath = document.getElementById('recordPath').value;
        const recordFileName = document.getElementById('recordFileName').value;
        const startDelay = document.getElementById('startDelay').value;
        const endDelay = document.getElementById('endDelay').value;
        const mergeTagBehavior = Number.parseInt(document.getElementById('mergeTagBehavior').value, 10);
        const isEnabled = document.getElementById('enabled').checked;
        const requestBody = {
            id: reserveEntry.id,
            sortOrder: reserveEntry.sortOrder,
            selectedRadikoStationIds: selectedRadikoStationIds,
            selectedRadiruStationIds: selectedRadiruStationIds,
            keyword: keyword,
            searchTitleOnly: searchTitleOnly,
            excludedKeyword: excludedKeyword,
            excludeTitleOnly: excludeTitleOnly,
            selectedDaysOfWeek: selectedDaysOfWeek,
            recordPath: recordPath,
            recordFileName: recordFileName,
            startTimeString: startTime,
            endTimeString: endTime,
            isEnabled: isEnabled,
            startDelay: parseInt(startDelay),
            endDelay: parseInt(endDelay),
            tagIds: selectedTagIds,
            mergeTagBehavior: Number.isNaN(mergeTagBehavior) ? 0 : mergeTagBehavior
        };
        try {
            const response = await fetch(API_ENDPOINTS.UPDATE_KEYWORD_RESERVE, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(requestBody)
            });
            const result = await response.json();
            if (result.success) {
                showGlobalToast(result.message ?? "更新しました。");
                loadRecordings();
                document.getElementById("keyword-reserve-modal")?.remove();
            }
            else {
                showGlobalToast(result.message ?? "更新に失敗しました。", false);
            }
        }
        catch (error) {
            console.error('Error:', error);
            showGlobalToast('エラーが発生しました。', false);
        }
    });
    document.body.appendChild(modal);
}
//# sourceMappingURL=keyword-reserve.js.map