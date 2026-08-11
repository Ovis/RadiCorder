import { createCard, showModal, closeModal } from './cardUtil.js';
import { API_ENDPOINTS } from './const.js';
import { AvailabilityTimeFree, RecordingType, RadioServiceKind } from './define.js';
import { showConfirmDialog } from './feedback.js';
import { generateStationList } from './stationList.js';
import { parseUtcDateTime, sanitizeHtml } from './utils.js';
import { clearMultiSelect, renderSelectedTagChips, enableTouchLikeMultiSelect } from './tag-select-ui.js';
import { createInlineToast, wireInlineToastClose } from './inline-toast.js';
import { setOverlayLoading } from './loading.js';
import { createStandardPlayerJumpControls } from './player-jump-controls.js';
import { applyPlaybackRate, playerPlaybackRateOptions } from './player-rate-control.js';
import { clearPersistedPlayerState, writePersistedPlayerState } from './player-state-store.js';
const reservedRecordingKeys = new Set();
let availableTags = [];
const normalizeTagName = (value) => value.trim().toLocaleLowerCase();
const showSearchToast = createInlineToast('search-result-toast', 'search-result-toast-message');
let currentPlayingSourceUrl = null;
let currentPlayingSourceToken = null;
let currentPlayingProgramTitle = null;
function createTemplateTokenHelp(sectionLabel, targetInputId) {
    const wrapper = document.createElement('div');
    wrapper.className = 'mt-2';
    wrapper.innerHTML = `
        <details class="rk-token-help">
            <summary class="rk-token-help-title cursor-pointer">置換文字列一覧（${sectionLabel}）</summary>
            <div class="content mt-2">
                <p class="rk-token-help-note">以下の文字列は保存時に実際の番組情報へ置換されます。</p>
                <h4 class="rk-token-help-title">共通</h4>
                <div class="rk-token-help-grid">
                    <div class="rk-token-help-item"><code data-token="$StationId$" class="cursor-pointer" title="クリックで追加">$StationId$</code><span>放送局ID（例: QRR）</span></div>
                    <div class="rk-token-help-item"><code data-token="$StationName$" class="cursor-pointer" title="クリックで追加">$StationName$</code><span>放送局名（例: 文化放送）</span></div>
                    <div class="rk-token-help-item"><code data-token="$Title$" class="cursor-pointer" title="クリックで追加">$Title$</code><span>番組名（例: ニュース・交通情報）</span></div>
                </div>
                <h4 class="rk-token-help-title">放送開始日時</h4>
                <div class="rk-token-help-grid">
                    <div class="rk-token-help-item"><code data-token="$SYYYY$" class="cursor-pointer" title="クリックで追加">$SYYYY$</code><span>開始年4桁（例: 2024）</span></div>
                    <div class="rk-token-help-item"><code data-token="$SYY$" class="cursor-pointer" title="クリックで追加">$SYY$</code><span>開始年2桁（例: 24）</span></div>
                    <div class="rk-token-help-item"><code data-token="$SMM$" class="cursor-pointer" title="クリックで追加">$SMM$</code><span>開始月2桁（例: 04）</span></div>
                    <div class="rk-token-help-item"><code data-token="$SM$" class="cursor-pointer" title="クリックで追加">$SM$</code><span>開始月（例: 4）</span></div>
                    <div class="rk-token-help-item"><code data-token="$SDD$" class="cursor-pointer" title="クリックで追加">$SDD$</code><span>開始日2桁（例: 01）</span></div>
                    <div class="rk-token-help-item"><code data-token="$SD$" class="cursor-pointer" title="クリックで追加">$SD$</code><span>開始日（例: 1）</span></div>
                    <div class="rk-token-help-item"><code data-token="$STHH$" class="cursor-pointer" title="クリックで追加">$STHH$</code><span>開始時2桁（例: 09）</span></div>
                    <div class="rk-token-help-item"><code data-token="$STH$" class="cursor-pointer" title="クリックで追加">$STH$</code><span>開始時（例: 9）</span></div>
                    <div class="rk-token-help-item"><code data-token="$STMM$" class="cursor-pointer" title="クリックで追加">$STMM$</code><span>開始分2桁（例: 00）</span></div>
                    <div class="rk-token-help-item"><code data-token="$STM$" class="cursor-pointer" title="クリックで追加">$STM$</code><span>開始分（例: 0）</span></div>
                    <div class="rk-token-help-item"><code data-token="$STSS$" class="cursor-pointer" title="クリックで追加">$STSS$</code><span>開始秒2桁（例: 00）</span></div>
                    <div class="rk-token-help-item"><code data-token="$STS$" class="cursor-pointer" title="クリックで追加">$STS$</code><span>開始秒（例: 0）</span></div>
                </div>
                <h4 class="rk-token-help-title">放送終了日時</h4>
                <div class="rk-token-help-grid">
                    <div class="rk-token-help-item"><code data-token="$EYYYY$" class="cursor-pointer" title="クリックで追加">$EYYYY$</code><span>終了年4桁（例: 2024）</span></div>
                    <div class="rk-token-help-item"><code data-token="$EYY$" class="cursor-pointer" title="クリックで追加">$EYY$</code><span>終了年2桁（例: 24）</span></div>
                    <div class="rk-token-help-item"><code data-token="$EMM$" class="cursor-pointer" title="クリックで追加">$EMM$</code><span>終了月2桁（例: 04）</span></div>
                    <div class="rk-token-help-item"><code data-token="$EM$" class="cursor-pointer" title="クリックで追加">$EM$</code><span>終了月（例: 4）</span></div>
                    <div class="rk-token-help-item"><code data-token="$EDD$" class="cursor-pointer" title="クリックで追加">$EDD$</code><span>終了日2桁（例: 01）</span></div>
                    <div class="rk-token-help-item"><code data-token="$ED$" class="cursor-pointer" title="クリックで追加">$ED$</code><span>終了日（例: 1）</span></div>
                    <div class="rk-token-help-item"><code data-token="$ETHH$" class="cursor-pointer" title="クリックで追加">$ETHH$</code><span>終了時2桁（例: 09）</span></div>
                    <div class="rk-token-help-item"><code data-token="$ETH$" class="cursor-pointer" title="クリックで追加">$ETH$</code><span>終了時（例: 9）</span></div>
                    <div class="rk-token-help-item"><code data-token="$ETMM$" class="cursor-pointer" title="クリックで追加">$ETMM$</code><span>終了分2桁（例: 00）</span></div>
                    <div class="rk-token-help-item"><code data-token="$ETM$" class="cursor-pointer" title="クリックで追加">$ETM$</code><span>終了分（例: 0）</span></div>
                    <div class="rk-token-help-item"><code data-token="$ETSS$" class="cursor-pointer" title="クリックで追加">$ETSS$</code><span>終了秒2桁（例: 00）</span></div>
                    <div class="rk-token-help-item"><code data-token="$ETS$" class="cursor-pointer" title="クリックで追加">$ETS$</code><span>終了秒（例: 0）</span></div>
                </div>
            </div>
        </details>
    `;
    const tokenElements = wrapper.querySelectorAll('code[data-token]');
    tokenElements.forEach((tokenElm) => {
        tokenElm.addEventListener('click', () => {
            const input = document.getElementById(targetInputId);
            const token = tokenElm.dataset.token ?? '';
            if (!input || !token) {
                return;
            }
            input.value += token;
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.focus();
            const end = input.value.length;
            input.setSelectionRange(end, end);
        });
    });
    return wrapper;
}
async function reserveProgramWithToast(programId, serviceKind, recordingType, button) {
    const reservationKey = `${programId}:${serviceKind}:${recordingType}`;
    if (reservedRecordingKeys.has(reservationKey)) {
        return;
    }
    button.disabled = true;
    button.classList.add('opacity-70');
    try {
        const requestBody = {
            programId: programId,
            radioServiceKind: serviceKind,
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
            showSearchToast(result.message ?? '録音予約を開始しました。', true);
            return;
        }
        showSearchToast(result?.message ?? '録音予約に失敗しました。', false);
    }
    catch (error) {
        console.error('Error:', error);
        showSearchToast('録音予約に失敗しました。', false);
    }
    button.disabled = false;
    button.classList.remove('opacity-70');
}
function persistCurrentPlaybackState() {
    const audio = document.getElementById('audio-player-elm');
    if (!currentPlayingSourceUrl || !audio) {
        return;
    }
    writePersistedPlayerState({
        sourceUrl: currentPlayingSourceUrl,
        sourceToken: currentPlayingSourceToken,
        title: currentPlayingProgramTitle,
        currentTime: Number.isFinite(audio.currentTime) ? audio.currentTime : 0,
        playbackRate: Number.isFinite(audio.playbackRate) ? audio.playbackRate : 1,
        wasPlaying: !audio.paused && !audio.ended,
        savedAtUtc: new Date().toISOString()
    });
}
async function playProgramWithToast(program) {
    const requestBody = {
        programId: program.programId,
        radioServiceKind: program.serviceKind
    };
    try {
        const response = await fetch(API_ENDPOINTS.PROGRAM_PLAY, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(requestBody)
        });
        if (!response.ok) {
            const failedResult = await response.json();
            showSearchToast(failedResult.message ?? '再生に失敗しました。', false);
            return;
        }
        const result = await response.json();
        const data = result.data;
        if (!data?.url) {
            showSearchToast(result.message ?? '再生に失敗しました。', false);
            return;
        }
        const sourceToken = program.serviceKind === RadioServiceKind.Radiko ? data.token : null;
        await playProgramFromSource(data.url, sourceToken, program.title, 0, playerPlaybackRateOptions[0]);
    }
    catch (error) {
        console.error('Error:', error);
        showSearchToast('再生に失敗しました。', false);
    }
}
async function playProgramFromSource(sourceUrl, sourceToken, programTitle, startTimeSeconds, playbackRate) {
    const footer = document.getElementById('audio-player');
    if (!footer) {
        showSearchToast('プレイヤーの初期化に失敗しました。', false);
        return;
    }
    let audio = document.getElementById('audio-player-elm');
    if (!audio) {
        footer.innerHTML = '';
        const playerContainerElm = document.createElement('div');
        playerContainerElm.className = 'player-container';
        const playerMainRowElm = document.createElement('div');
        playerMainRowElm.className = 'player-main-row';
        const audioPlayerElm = document.createElement('audio');
        audioPlayerElm.id = 'audio-player-elm';
        audioPlayerElm.style.width = '100%';
        audioPlayerElm.style.height = '2rem';
        audioPlayerElm.controls = true;
        audioPlayerElm.addEventListener('timeupdate', persistCurrentPlaybackState);
        audioPlayerElm.addEventListener('pause', persistCurrentPlaybackState);
        audioPlayerElm.addEventListener('ratechange', persistCurrentPlaybackState);
        const closeButton = document.createElement('button');
        closeButton.type = 'button';
        closeButton.className = 'player-close-button';
        closeButton.setAttribute('aria-label', 'プレイヤーを閉じる');
        closeButton.innerHTML = '<i class="fas fa-xmark" aria-hidden="true"></i>';
        closeButton.addEventListener('click', () => {
            const player = document.getElementById('audio-player-elm');
            if (player) {
                player.pause();
                player.removeAttribute('src');
                player.load();
            }
            currentPlayingSourceUrl = null;
            currentPlayingSourceToken = null;
            currentPlayingProgramTitle = null;
            clearPersistedPlayerState();
            footer.innerHTML = '';
        });
        playerMainRowElm.appendChild(audioPlayerElm);
        playerMainRowElm.appendChild(closeButton);
        playerContainerElm.appendChild(playerMainRowElm);
        playerContainerElm.appendChild(createStandardPlayerJumpControls(audioPlayerElm));
        footer.appendChild(playerContainerElm);
        audio = audioPlayerElm;
    }
    const previousSourceUrl = currentPlayingSourceUrl;
    const previousSourceToken = currentPlayingSourceToken;
    currentPlayingSourceUrl = sourceUrl;
    currentPlayingSourceToken = sourceToken;
    currentPlayingProgramTitle = programTitle;
    const isSameSource = previousSourceUrl === sourceUrl &&
        (previousSourceToken ?? '') === (sourceToken ?? '');
    const effectivePlaybackRate = isSameSource ? playbackRate : playerPlaybackRateOptions[0];
    const hlsConstructor = window.Hls;
    if (hlsConstructor?.isSupported?.()) {
        const hls = new hlsConstructor();
        applyPlaybackRate(audio, effectivePlaybackRate);
        if (sourceToken) {
            hls.config.xhrSetup = function (xhr) {
                xhr.setRequestHeader('X-Radiko-AuthToken', sourceToken);
            };
        }
        hls.loadSource(sourceUrl);
        hls.attachMedia(audio);
        hls.on(hlsConstructor.Events.MANIFEST_PARSED, () => {
            if (startTimeSeconds > 0) {
                audio.currentTime = startTimeSeconds;
            }
            void audio.play();
        });
    }
    else if (audio.canPlayType('application/vnd.apple.mpegurl')) {
        applyPlaybackRate(audio, effectivePlaybackRate);
        audio.src = sourceUrl;
        audio.onloadedmetadata = () => {
            if (startTimeSeconds > 0) {
                audio.currentTime = startTimeSeconds;
            }
            void audio.play();
        };
    }
    else {
        showSearchToast('このブラウザはHLS再生に対応していません。', false);
        return;
    }
    persistCurrentPlaybackState();
}
document.querySelectorAll('.modal-closeProcess').forEach(elm => {
    elm.addEventListener('click', closeModal);
});
document.addEventListener('DOMContentLoaded', async () => {
    const radikoStationGroupsElm = document.getElementById('radikoStationGroups');
    const radiruStationGroupsElm = document.getElementById('radiruStationGroups');
    wireInlineToastClose('search-result-toast-close', 'search-result-toast');
    radikoStationGroupsElm.appendChild(await generateStationList(RadioServiceKind.Radiko));
    radiruStationGroupsElm.appendChild(await generateStationList(RadioServiceKind.Radiru));
    availableTags = await loadTags();
    renderOptionCard();
});
async function loadTags() {
    try {
        const response = await fetch(API_ENDPOINTS.TAGS);
        const result = await response.json();
        return result.data ?? [];
    }
    catch (error) {
        console.error('Error loading tags:', error);
        return [];
    }
}
function renderOptionCard() {
    const optionDivElm = document.getElementById('option');
    optionDivElm.replaceChildren();
    const contentElements = [];
    {
        const pathOptionCard = document.createElement('div');
        pathOptionCard.className = 'field pt-3';
        const label = document.createElement('label');
        label.className = 'label';
        label.textContent = '番組の保存先';
        const control = document.createElement('div');
        control.className = 'control';
        const input = document.createElement('input');
        input.className = 'input';
        input.type = 'text';
        input.id = 'recordPath';
        input.placeholder = '$StationName$\\$Title$\\$SYYYY$\\$SMM$\\';
        control.appendChild(input);
        pathOptionCard.appendChild(label);
        pathOptionCard.appendChild(control);
        pathOptionCard.appendChild(createTemplateTokenHelp('保存先パス', 'recordPath'));
        contentElements.push(pathOptionCard);
    }
    {
        const fileNameOptionCard = document.createElement('div');
        fileNameOptionCard.className = 'field pt-3';
        const label = document.createElement('label');
        label.className = 'label';
        label.textContent = '番組のファイル名';
        const control = document.createElement('div');
        control.className = 'control';
        const input = document.createElement('input');
        input.className = 'input';
        input.type = 'text';
        input.id = 'recordFileName';
        input.placeholder = '$SYYYY$$SMM$$SDD$_$Title$';
        control.appendChild(input);
        fileNameOptionCard.appendChild(label);
        fileNameOptionCard.appendChild(control);
        fileNameOptionCard.appendChild(createTemplateTokenHelp('ファイル名', 'recordFileName'));
        contentElements.push(fileNameOptionCard);
    }
    {
        const tagsOptionCard = document.createElement('div');
        tagsOptionCard.className = 'field pt-3';
        const label = document.createElement('label');
        label.className = 'label';
        label.textContent = 'タグ';
        const control = document.createElement('div');
        control.className = 'control';
        const layout = document.createElement('div');
        layout.className = 'rk-tag-select-layout';
        const select = document.createElement('select');
        select.className = 'input';
        select.id = 'keywordReserveTagIds';
        select.multiple = true;
        select.size = 5;
        select.ariaLabel = '自動予約ルールタグ';
        enableTouchLikeMultiSelect(select);
        availableTags.forEach((tag) => {
            const option = document.createElement('option');
            option.value = tag.id;
            option.textContent = tag.name;
            select.appendChild(option);
        });
        const selectedPanel = document.createElement('div');
        selectedPanel.className = 'rk-selected-tags-panel';
        const selectedHead = document.createElement('div');
        selectedHead.className = 'is-flex is-justify-content-space-between is-align-items-center';
        const selectedTitle = document.createElement('span');
        selectedTitle.className = 'is-size-7 has-text-weight-semibold';
        selectedTitle.textContent = '選択中タグ';
        const clearButton = document.createElement('button');
        clearButton.type = 'button';
        clearButton.id = 'keywordReserveTagIdsClear';
        clearButton.className = 'button is-small is-light';
        clearButton.textContent = 'すべて解除';
        selectedHead.appendChild(selectedTitle);
        selectedHead.appendChild(clearButton);
        const chips = document.createElement('div');
        chips.id = 'keywordReserveTagIdsChips';
        chips.className = 'rk-selected-tags-chip-list';
        selectedPanel.appendChild(selectedHead);
        selectedPanel.appendChild(chips);
        layout.appendChild(select);
        layout.appendChild(selectedPanel);
        control.appendChild(layout);
        const createInline = document.createElement('div');
        createInline.className = 'field has-addons rk-tag-create-inline';
        const createInputControl = document.createElement('div');
        createInputControl.className = 'control is-expanded';
        const createInput = document.createElement('input');
        createInput.className = 'input';
        createInput.type = 'text';
        createInput.id = 'keywordReserveTagCreateName';
        createInput.placeholder = '新しいタグ名';
        createInputControl.appendChild(createInput);
        const createButtonControl = document.createElement('div');
        createButtonControl.className = 'control';
        const createButton = document.createElement('button');
        createButton.type = 'button';
        createButton.id = 'keywordReserveTagCreateButton';
        createButton.className = 'button is-light';
        createButton.textContent = '+ 作成';
        createButtonControl.appendChild(createButton);
        createInline.appendChild(createInputControl);
        createInline.appendChild(createButtonControl);
        control.appendChild(createInline);
        const createSuggestions = document.createElement('div');
        createSuggestions.id = 'keywordReserveTagCreateSuggestions';
        createSuggestions.className = 'rk-tag-create-suggestion-list';
        createSuggestions.setAttribute('aria-live', 'polite');
        control.appendChild(createSuggestions);
        tagsOptionCard.appendChild(label);
        tagsOptionCard.appendChild(control);
        select.addEventListener('change', () => renderSelectedTagChips(select, chips));
        clearButton.addEventListener('click', () => {
            clearMultiSelect(select);
            renderSelectedTagChips(select, chips);
        });
        const renderTagCreateSuggestions = () => {
            createSuggestions.innerHTML = '';
            const keyword = createInput.value.trim();
            if (!keyword) {
                return;
            }
            const normalizedKeyword = normalizeTagName(keyword);
            const exact = availableTags.find((tag) => normalizeTagName(tag.name) === normalizedKeyword);
            if (!exact) {
                const createSuggestButton = document.createElement('button');
                createSuggestButton.type = 'button';
                createSuggestButton.className = 'button is-small is-info is-light';
                createSuggestButton.textContent = `「${keyword}」を新規作成`;
                createSuggestButton.addEventListener('click', () => {
                    createButton.click();
                });
                createSuggestions.appendChild(createSuggestButton);
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
                    Array.from(select.options).forEach((option) => {
                        if (option.value === tag.id) {
                            option.selected = true;
                        }
                    });
                    select.dispatchEvent(new Event('change'));
                    createInput.value = '';
                    createSuggestions.innerHTML = '';
                    showSearchToast(`タグ「${tag.name}」を選択しました。`, true);
                });
                createSuggestions.appendChild(button);
            });
        };
        createInput.addEventListener('input', renderTagCreateSuggestions);
        createInput.addEventListener('keydown', (event) => {
            if (event.key === 'Enter') {
                event.preventDefault();
            }
        });
        createButton.addEventListener('click', async () => {
            const name = createInput.value.trim();
            if (!name) {
                showSearchToast('タグ名を入力してください。', false);
                return;
            }
            const normalizedName = normalizeTagName(name);
            const existing = availableTags.find((tag) => normalizeTagName(tag.name) === normalizedName);
            if (existing) {
                Array.from(select.options).forEach((option) => {
                    if (option.value === existing.id) {
                        option.selected = true;
                    }
                });
                select.dispatchEvent(new Event('change'));
                createInput.value = '';
                renderTagCreateSuggestions();
                showSearchToast(`タグ「${existing.name}」を選択しました。`, true);
                return;
            }
            const confirmed = await showConfirmDialog(`タグ「${name}」を作成しますか？`, { okText: '作成する' });
            if (!confirmed) {
                return;
            }
            try {
                const selectedIds = new Set(Array.from(select.selectedOptions).map((option) => option.value));
                const requestBody = { name };
                const response = await fetch(API_ENDPOINTS.TAGS, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(requestBody)
                });
                const result = await response.json();
                if (!response.ok || !result.success) {
                    showSearchToast(result.message ?? 'タグ作成に失敗しました。', false);
                    return;
                }
                const createdTagId = result.data?.id ?? '';
                if (createdTagId) {
                    selectedIds.add(createdTagId);
                }
                availableTags = await loadTags();
                select.innerHTML = '';
                availableTags.forEach((tag) => {
                    const option = document.createElement('option');
                    option.value = tag.id;
                    option.textContent = tag.name;
                    option.selected = selectedIds.has(tag.id);
                    select.appendChild(option);
                });
                renderSelectedTagChips(select, chips);
                createInput.value = '';
                renderTagCreateSuggestions();
                showSearchToast(result.message ?? 'タグを作成しました。', true);
            }
            catch (error) {
                console.error('Error:', error);
                showSearchToast('タグ作成に失敗しました。', false);
            }
        });
        renderSelectedTagChips(select, chips);
        contentElements.push(tagsOptionCard);
    }
    {
        const mergeTagBehavior = document.createElement('div');
        mergeTagBehavior.className = 'field pt-3';
        const label = document.createElement('label');
        label.className = 'label';
        label.textContent = 'タグ付与方式（複数ルール一致時）';
        const control = document.createElement('div');
        control.className = 'control';
        const selectWrap = document.createElement('div');
        selectWrap.className = 'select is-fullwidth';
        const select = document.createElement('select');
        select.id = 'keywordReserveMergeTagBehavior';
        const options = [
            { value: '0', text: '全体設定に従う' },
            { value: '1', text: '常にこのルールのタグを追加する' },
            { value: '2', text: '他ルールも一致したときは、このルールのタグを追加しない' }
        ];
        options.forEach((item) => {
            const option = document.createElement('option');
            option.value = item.value;
            option.textContent = item.text;
            select.appendChild(option);
        });
        selectWrap.appendChild(select);
        control.appendChild(selectWrap);
        mergeTagBehavior.appendChild(label);
        mergeTagBehavior.appendChild(control);
        const help = document.createElement('p');
        help.className = 'help';
        help.textContent = '同じ番組に複数ルールが一致したときの、このルールのタグ追加方法を指定します。';
        mergeTagBehavior.appendChild(help);
        contentElements.push(mergeTagBehavior);
    }
    {
        const startDelay = document.createElement('div');
        startDelay.className = 'field pt-3';
        const label = document.createElement('label');
        label.className = 'label';
        label.textContent = 'リアルタイム予約時の開始時間マージン（秒）';
        const control = document.createElement('div');
        control.className = 'control';
        const input = document.createElement('input');
        input.className = 'input';
        input.type = 'number';
        input.pattern = '[0-9]*';
        input.id = 'startDelay';
        input.onkeydown = (e) => { return e.key != "e"; };
        control.appendChild(input);
        startDelay.appendChild(label);
        startDelay.appendChild(control);
        contentElements.push(startDelay);
    }
    {
        const endDelay = document.createElement('div');
        endDelay.className = 'field pt-3';
        const label = document.createElement('label');
        label.className = 'label';
        label.textContent = 'リアルタイム予約時の終了時間マージン（秒）';
        const control = document.createElement('div');
        control.className = 'control';
        const input = document.createElement('input');
        input.className = 'input';
        input.type = 'number';
        input.id = 'endDelay';
        input.onkeydown = (e) => { return e.key != "e"; };
        control.appendChild(input);
        endDelay.appendChild(label);
        endDelay.appendChild(control);
        contentElements.push(endDelay);
    }
    const cardHeaderTitle = document.createElement('p');
    cardHeaderTitle.innerHTML = `自動予約ルールのオプション`;
    const cardElm = createCard("optionCard", cardHeaderTitle, contentElements);
    optionDivElm.appendChild(cardElm);
}
document.getElementById('searchButton').addEventListener('click', async function () {
    const searchButton = this;
    const searchLoadingOverlay = document.getElementById('searchLoadingOverlay');
    const selectedRadikoStationIds = Array.from(document.querySelectorAll('input[name="SelectedRadikoStationIds"]:checked')).map(checkbox => checkbox.value);
    const selectedRadiruStationIds = Array.from(document.querySelectorAll('input[name="SelectedRadiruStationIds"]:checked')).map(checkbox => checkbox.value);
    const keyword = document.getElementById('Keyword').value;
    const searchTitleOnly = document.getElementById('SearchTitleOnly').checked;
    const excludedKeyword = document.getElementById('ExcludedKeyword').value;
    const excludeTitleOnly = document.getElementById('ExcludeTitleOnly').checked;
    const selectedDaysOfWeek = Array.from(document.querySelectorAll('input[name="SelectedDaysOfWeek"]:checked')).map(checkbox => parseInt(checkbox.value));
    const startTimeInput = document.getElementById('StartTime').value;
    const endTimeInput = document.getElementById('EndTime').value;
    const startTime = `${startTimeInput}:00`;
    const endTime = `${endTimeInput}:00`;
    const includeHistoricalPrograms = document.getElementById('IncludeHistoricalPrograms').checked;
    const recordableOnly = document.getElementById('RecordableOnly').checked;
    const order = document.getElementById('KeywordReserveOrderKind').value;
    // selectedDaysOfWeekが空の場合はアラートを出して終了
    if (selectedDaysOfWeek.length === 0) {
        showModal('曜日を選択してください。');
        return;
    }
    const requestBody = {
        selectedRadikoStationIds: selectedRadikoStationIds,
        selectedRadiruStationIds: selectedRadiruStationIds,
        keyword: keyword,
        searchTitleOnly: searchTitleOnly,
        excludedKeyword: excludedKeyword,
        searchTitleOnlyExcludedKeyword: excludeTitleOnly,
        selectedDaysOfWeek: selectedDaysOfWeek,
        startTime: startTime,
        endTime: endTime,
        includeHistoricalPrograms: includeHistoricalPrograms,
        recordableOnly: recordableOnly,
        orderKind: order
    };
    try {
        searchButton.disabled = true;
        if (searchLoadingOverlay) {
            setOverlayLoading(searchLoadingOverlay, true, { busyText: '検索中...' });
        }
        const response = await fetch(API_ENDPOINTS.PROGRAM_SEARCH, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(requestBody)
        });
        const result = await response.json();
        const programs = result.data ?? [];
        const searchResultElm = document.getElementById('searchResult');
        const searchResultEmptyElm = document.getElementById('searchResultEmpty');
        const template = document.getElementById('search-program-card-template');
        searchResultElm.replaceChildren();
        if (programs && programs.length > 0 && template) {
            searchResultEmptyElm?.classList.add('hidden');
            const sortedPrograms = [...programs].sort((a, b) => new Date(a.startTime).getTime() - new Date(b.startTime).getTime());
            sortedPrograms.forEach(program => {
                const startTime = new Date(program.startTime);
                const endTime = new Date(program.endTime);
                const card = template.content.cloneNode(true);
                const now = Date.now();
                const titleElm = card.querySelector('.title');
                const statusBadgeElm = card.querySelector('.status-badge');
                const subtitleElm = card.querySelector('.subtitle');
                const performerElm = card.querySelector('.performer');
                const startElm = card.querySelector('.startTime');
                const endElm = card.querySelector('.endTime');
                const descriptionElm = card.querySelector('.description');
                const descriptionToggleElm = card.querySelector('.description-toggle');
                const playBtn = card.querySelector('.play-btn');
                const timeFreeBtn = card.querySelector('.timefree-btn');
                const realtimeBtn = card.querySelector('.record-btn');
                const onDemandBtn = card.querySelector('.ondemand-btn');
                if (titleElm) {
                    titleElm.textContent = program.title;
                }
                if (statusBadgeElm) {
                    const current = new Date();
                    statusBadgeElm.classList.add('hidden');
                    statusBadgeElm.classList.remove('is-live', 'is-ended');
                    if (startTime <= current && current < endTime) {
                        statusBadgeElm.textContent = 'Now';
                        statusBadgeElm.classList.add('is-live');
                        statusBadgeElm.classList.remove('hidden');
                    }
                    else if (endTime <= current) {
                        statusBadgeElm.textContent = 'Ended';
                        statusBadgeElm.classList.add('is-ended');
                        statusBadgeElm.classList.remove('hidden');
                    }
                }
                if (subtitleElm) {
                    subtitleElm.textContent = program.serviceKind === RadioServiceKind.Radiko
                        ? `${program.stationName} (${program.stationId})`
                        : `${program.stationName} (${program.areaName})`;
                }
                if (performerElm) {
                    performerElm.textContent = program.performer ? `出演: ${program.performer}` : '';
                }
                if (startElm) {
                    startElm.textContent = `開始時間: ${startTime.toLocaleString()}`;
                }
                if (endElm) {
                    endElm.textContent = `終了時間: ${endTime.toLocaleString()}`;
                }
                const descriptionText = program.description ?? '';
                if (descriptionElm) {
                    descriptionElm.innerHTML = sanitizeHtml(descriptionText);
                }
                if (descriptionElm && descriptionToggleElm) {
                    const plainText = descriptionText.replace(/<[^>]*>/g, '').trim();
                    if (plainText.length > 140) {
                        descriptionElm.classList.add('is-collapsed');
                        descriptionToggleElm.classList.remove('hidden');
                        descriptionToggleElm.addEventListener('click', () => {
                            const isCollapsed = descriptionElm.classList.contains('is-collapsed');
                            if (isCollapsed) {
                                descriptionElm.classList.remove('is-collapsed');
                                descriptionToggleElm.textContent = 'less';
                            }
                            else {
                                descriptionElm.classList.add('is-collapsed');
                                descriptionToggleElm.textContent = 'more';
                            }
                        });
                    }
                    else {
                        descriptionToggleElm.classList.add('hidden');
                    }
                }
                const isTimeFreeAvailable = program.availabilityTimeFree === AvailabilityTimeFree.Available ||
                    program.availabilityTimeFree === AvailabilityTimeFree.PartiallyAvailable;
                const onDemandExpiresAt = parseUtcDateTime(program.onDemandExpiresAtUtc)?.getTime() ?? Number.NaN;
                const isOnDemandAvailable = program.serviceKind === RadioServiceKind.Radiru &&
                    !!program.onDemandContentUrl &&
                    Number.isFinite(onDemandExpiresAt) &&
                    onDemandExpiresAt > now;
                const isCurrentlyOnAir = startTime.getTime() <= now && endTime.getTime() > now;
                const canPlay = isCurrentlyOnAir;
                if (playBtn) {
                    if (canPlay) {
                        playBtn.classList.remove('hidden');
                        playBtn.onclick = () => {
                            void playProgramWithToast(program);
                        };
                    }
                    else {
                        playBtn.classList.add('hidden');
                    }
                }
                if (timeFreeBtn) {
                    if (isTimeFreeAvailable) {
                        timeFreeBtn.onclick = () => reserveProgramWithToast(program.programId, program.serviceKind, RecordingType.TimeFree, timeFreeBtn);
                    }
                    else {
                        timeFreeBtn.classList.add('hidden');
                    }
                }
                if (realtimeBtn) {
                    if (endTime.getTime() > now) {
                        realtimeBtn.onclick = () => {
                            if (endTime.getTime() <= Date.now()) {
                                showSearchToast('この番組はすでに終了しているため、リアルタイム録音できません。', false);
                                return;
                            }
                            reserveProgramWithToast(program.programId, program.serviceKind, RecordingType.RealTime, realtimeBtn);
                        };
                    }
                    else {
                        realtimeBtn.classList.add('hidden');
                    }
                }
                if (onDemandBtn) {
                    if (isOnDemandAvailable) {
                        onDemandBtn.classList.remove('hidden');
                        onDemandBtn.textContent = '聞き逃し配信録音';
                        onDemandBtn.disabled = false;
                        onDemandBtn.onclick = () => reserveProgramWithToast(program.programId, program.serviceKind, RecordingType.OnDemand, onDemandBtn);
                    }
                    else if (program.serviceKind === RadioServiceKind.Radiru && !!program.onDemandContentUrl) {
                        onDemandBtn.classList.remove('hidden');
                        onDemandBtn.textContent = '聞き逃し期限切れ';
                        onDemandBtn.disabled = true;
                    }
                    else {
                        onDemandBtn.classList.add('hidden');
                    }
                }
                searchResultElm.appendChild(card);
            });
        }
        else {
            searchResultEmptyElm?.classList.remove('hidden');
        }
    }
    catch (e) {
        console.error(e);
        showSearchToast('検索に失敗しました。', false);
    }
    finally {
        searchButton.disabled = false;
        if (searchLoadingOverlay) {
            setOverlayLoading(searchLoadingOverlay, false);
        }
    }
});
// 自動予約ルール追加ボタン
document.getElementById('recordingButton').addEventListener('click', async function () {
    const selectedRadikoStationIds = Array.from(document.querySelectorAll('input[name="SelectedRadikoStationIds"]:checked')).map(checkbox => checkbox.value);
    const selectedRadiruStationIds = Array.from(document.querySelectorAll('input[name="SelectedRadiruStationIds"]:checked')).map(checkbox => checkbox.value);
    const keyword = document.getElementById('Keyword').value;
    const searchTitleOnly = document.getElementById('SearchTitleOnly').checked;
    const excludedKeyword = document.getElementById('ExcludedKeyword').value;
    const excludeTitleOnly = document.getElementById('ExcludeTitleOnly').checked;
    const selectedDaysOfWeek = Array.from(document.querySelectorAll('input[name="SelectedDaysOfWeek"]:checked')).map(checkbox => parseInt(checkbox.value));
    const startTimeInput = document.getElementById('StartTime').value;
    const endTimeInput = document.getElementById('EndTime').value;
    const startDelayInput = document.getElementById('startDelay').value;
    const endDelayInput = document.getElementById('endDelay').value;
    const mergeTagBehaviorInput = document.getElementById('keywordReserveMergeTagBehavior')?.value ?? '0';
    const selectedTagIds = Array.from(document.querySelectorAll('#keywordReserveTagIds option:checked')).map(option => option.value);
    const recordPath = document.getElementById('recordPath').value;
    const recordFileName = document.getElementById('recordFileName').value;
    // selectedDaysOfWeekが空の場合はアラートを出して終了
    if (selectedDaysOfWeek.length === 0) {
        showModal('曜日を選択してください。');
        return;
    }
    // キーワード欄未入力の場合はアラートを出して終了
    if (keyword === '') {
        showModal('キーワードを入力してください。');
        return;
    }
    const requestBody = {
        selectedRadikoStationIds: selectedRadikoStationIds,
        selectedRadiruStationIds: selectedRadiruStationIds,
        keyword: keyword,
        searchTitleOnly: searchTitleOnly,
        excludedKeyword: excludedKeyword,
        excludeTitleOnly: excludeTitleOnly,
        selectedDaysOfWeek: selectedDaysOfWeek,
        recordPath: recordPath,
        recordFileName: recordFileName,
        startTimeString: startTimeInput,
        endTimeString: endTimeInput,
        isEnabled: true,
        startDelay: parseInt(startDelayInput),
        endDelay: parseInt(endDelayInput),
        tagIds: selectedTagIds,
        mergeTagBehavior: Number.parseInt(mergeTagBehaviorInput, 10)
    };
    try {
        const response = await fetch(API_ENDPOINTS.KEYWORD_RESERVE, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(requestBody)
        });
        const result = await response.json();
        if (result.success) {
            showSearchToast("自動予約ルールを追加しました。");
        }
        else {
            showSearchToast(result.message ?? "録音予約に失敗しました。", false);
        }
    }
    catch (e) {
        showSearchToast("録音予約に失敗しました。", false);
    }
});
//# sourceMappingURL=search.js.map