import { RadioServiceKind, RadioServiceKindMap } from './define.js';
import { RecordingType } from './define.js';
import { AvailabilityTimeFree } from './define.js';
import { API_ENDPOINTS } from './const.js';
import { showGlobalToast } from './feedback.js';
import { setTextContent, setInnerHtml, setEventListener, sanitizeHtml } from './utils.js';
import { playerPlaybackRateOptions } from './player-rate-control.js';
import { createStandardPlayerJumpControls } from './player-jump-controls.js';
import { configurePlayer, getPlayerHls, playPlayerSource, resumePlayer } from './player-controller.js';
import { registerPage } from './page-navigation.js';
registerPage('home.js', async (signal) => {
    configurePlayer({
        createControls: (audio, state) => state?.kind === 'live'
            ? createPlayerJumpControls(audio)
            : createStandardPlayerJumpControls(audio)
    });
    try {
        const response = await fetch(API_ENDPOINTS.PROGRAM_NOW);
        const result = await response.json();
        if (signal.aborted) {
            return;
        }
        const data = result.data;
        const template = document.getElementById('program-card-template');
        const container = document.getElementById('programs-container');
        const emptyState = document.getElementById('programs-empty');
        const tabsWrapper = document.getElementById('area-tabs-wrapper');
        const tabsContainer = document.getElementById('area-tabs');
        const searchInput = document.getElementById('now-search');
        const resultToast = document.getElementById('home-result-toast');
        const resultToastMessage = document.getElementById('home-result-toast-message');
        const resultToastClose = document.getElementById('home-result-toast-close');
        let toastTimerId;
        const reservedRecordingKeys = new Set();
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
        if (resultToastClose) {
            resultToastClose.addEventListener('click', () => {
                if (resultToast) {
                    resultToast.classList.remove('is-active');
                }
            });
        }
        const reserveProgramByType = async (programId, serviceKind, recordingType, button) => {
            const reservationKey = `${programId}:${recordingType}`;
            if (reservedRecordingKeys.has(reservationKey)) {
                return;
            }
            button.classList.add('is-static');
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
                    button.classList.add('pointer-events-none');
                    button.textContent = '予約済み';
                    showToast(result.message ?? '録音予約を開始しました。', true);
                    return;
                }
                showToast(result?.message ?? '録音予約に失敗しました。', false);
            }
            catch (error) {
                console.error('Error:', error);
                showToast('録音予約に失敗しました。', false);
            }
            button.classList.remove('is-static');
            button.classList.remove('opacity-70');
        };
        const setupDescriptionToggle = (card, descriptionHtml) => {
            const descriptionElement = card.querySelector(".description");
            const toggleElement = card.querySelector(".description-toggle");
            if (!descriptionElement || !toggleElement) {
                return;
            }
            const plainText = (descriptionHtml ?? "").replace(/<[^>]*>/g, "").trim();
            const shouldCollapse = plainText.length > 160;
            if (!shouldCollapse) {
                toggleElement.classList.add("hidden");
                descriptionElement.classList.remove("is-collapsed");
                return;
            }
            descriptionElement.classList.add("is-collapsed");
            toggleElement.classList.remove("hidden");
            toggleElement.textContent = "more";
            toggleElement.addEventListener("click", () => {
                const isCollapsed = descriptionElement.classList.contains("is-collapsed");
                if (isCollapsed) {
                    descriptionElement.classList.remove("is-collapsed");
                    toggleElement.textContent = "less";
                    return;
                }
                descriptionElement.classList.add("is-collapsed");
                toggleElement.textContent = "more";
            });
        };
        const isProgramEnded = (endTime) => {
            return new Date(endTime).getTime() <= Date.now();
        };
        let programs = [];
        let areas = [];
        let currentAreaStations = [];
        let activeServiceKind = RadioServiceKind.Undefined;
        let activeAreaId = '';
        const activeAreaByService = new Map();
        programs = data.programs ?? [];
        // OpenAPI生成型では数値が string になる場合があるため、画面ロジック用に数値化する。
        areas = (data.areas ?? []).map((x) => ({
            ...x,
            areaOrder: Number(x.areaOrder),
            serviceOrder: Number(x.serviceOrder)
        }));
        currentAreaStations = data.currentAreaStations ?? [];
        const normalizeAreas = () => {
            if (areas.length > 0) {
                return areas;
            }
            const dedup = new Map();
            programs.forEach(program => {
                const areaId = (program.areaId ?? '').trim();
                const areaName = (program.areaName ?? '').trim();
                if (areaId.length > 0 && !dedup.has(areaId)) {
                    dedup.set(areaId, areaName.length > 0 ? areaName : areaId);
                }
            });
            return Array.from(dedup.entries()).map(([areaId, areaName], index) => ({
                areaId,
                areaName,
                areaOrder: index,
                serviceOrder: 0
            }));
        };
        areas = normalizeAreas();
        const areaOrderMap = new Map(areas.map(area => [area.areaId, area.areaOrder]));
        const serviceKinds = [RadioServiceKind.Radiko, RadioServiceKind.Radiru]
            .filter(serviceKind => programs.some(program => program.serviceKind === serviceKind));
        const getAreasForService = (serviceKind) => {
            const dedup = new Map();
            programs
                .filter(program => program.serviceKind === serviceKind)
                .forEach(program => {
                const areaId = (program.areaId ?? '').trim();
                const areaName = (program.areaName ?? '').trim();
                if (areaId.length === 0 || dedup.has(areaId)) {
                    return;
                }
                dedup.set(areaId, areaName.length > 0 ? areaName : areaId);
            });
            return Array.from(dedup.entries())
                .map(([areaId, areaName], index) => ({
                areaId,
                areaName,
                areaOrder: areaOrderMap.get(areaId) ?? index
            }))
                .sort((a, b) => a.areaOrder - b.areaOrder || a.areaName.localeCompare(b.areaName));
        };
        const resolveCurrentAreaId = () => {
            if (currentAreaStations.length === 0) {
                return '';
            }
            const stationSet = new Set(currentAreaStations);
            const matched = programs.find(program => stationSet.has(program.stationId));
            return matched?.areaId ?? '';
        };
        const currentAreaId = resolveCurrentAreaId();
        activeServiceKind = serviceKinds.includes(RadioServiceKind.Radiko)
            ? RadioServiceKind.Radiko
            : (serviceKinds[0] ?? RadioServiceKind.Undefined);
        serviceKinds.forEach(serviceKind => {
            const serviceAreas = getAreasForService(serviceKind);
            activeAreaByService.set(serviceKind, serviceAreas[0]?.areaId ?? '');
        });
        if (activeServiceKind === RadioServiceKind.Radiko &&
            currentAreaId.length > 0 &&
            getAreasForService(RadioServiceKind.Radiko).some(area => area.areaId === currentAreaId)) {
            activeAreaByService.set(RadioServiceKind.Radiko, currentAreaId);
        }
        activeAreaId = activeAreaByService.get(activeServiceKind) ?? '';
        const buildTabs = () => {
            if (!tabsContainer || !tabsWrapper) {
                return;
            }
            tabsContainer.innerHTML = '';
            const activeServiceAreas = getAreasForService(activeServiceKind);
            const shouldShowTabs = serviceKinds.length > 1 || activeServiceAreas.length > 1;
            if (!shouldShowTabs) {
                tabsWrapper.classList.add('hidden');
                return;
            }
            tabsWrapper.classList.remove('hidden');
            tabsContainer.className = 'home-tabs';
            if (serviceKinds.length > 1) {
                const serviceGroup = document.createElement('div');
                serviceGroup.className = 'home-tabs-group';
                const serviceLabel = document.createElement('div');
                serviceLabel.className = 'home-tabs-label';
                serviceLabel.textContent = 'サービス';
                serviceGroup.appendChild(serviceLabel);
                const serviceRow = document.createElement('div');
                serviceRow.className = 'home-tabs-row is-service';
                serviceKinds.forEach(serviceKind => {
                    const button = document.createElement('button');
                    button.type = 'button';
                    button.className = 'button is-light tab-button';
                    button.textContent = RadioServiceKindMap[serviceKind].displayName;
                    button.dataset.serviceKind = serviceKind.toString();
                    button.setAttribute('aria-pressed', serviceKind === activeServiceKind ? 'true' : 'false');
                    button.classList.toggle('is-active', serviceKind === activeServiceKind);
                    button.addEventListener('click', () => setActiveServiceTab(serviceKind));
                    serviceRow.appendChild(button);
                });
                serviceGroup.appendChild(serviceRow);
                tabsContainer.appendChild(serviceGroup);
            }
            const areaGroup = document.createElement('div');
            areaGroup.className = 'home-tabs-group';
            const areaLabel = document.createElement('div');
            areaLabel.className = 'home-tabs-label';
            areaLabel.textContent = 'エリア';
            areaGroup.appendChild(areaLabel);
            const areaRow = document.createElement('div');
            areaRow.className = 'home-tabs-row is-area';
            activeServiceAreas.forEach(area => {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'button is-light tab-button';
                button.textContent = area.areaName;
                button.dataset.areaId = area.areaId;
                button.dataset.serviceKind = activeServiceKind.toString();
                button.setAttribute('aria-pressed', area.areaId === activeAreaId ? 'true' : 'false');
                button.classList.toggle('is-active', area.areaId === activeAreaId);
                button.addEventListener('click', () => setActiveAreaTab(area.areaId));
                areaRow.appendChild(button);
            });
            areaGroup.appendChild(areaRow);
            tabsContainer.appendChild(areaGroup);
        };
        const getFilteredPrograms = () => {
            const query = searchInput.value.trim().toLowerCase();
            let list = programs.filter(p => p.serviceKind === activeServiceKind);
            // 検索語がある場合はエリア選択に依存せず全エリアから検索する
            if (query.length === 0 && activeAreaId.length > 0) {
                list = list.filter(p => p.areaId === activeAreaId);
            }
            if (query.length > 0) {
                list = list.filter(p => (p.title ?? '').toLowerCase().includes(query) ||
                    (p.stationName ?? '').toLowerCase().includes(query));
            }
            return list;
        };
        const render = () => {
            if (!container || !template) {
                return;
            }
            while (container.firstChild) {
                container.removeChild(container.firstChild);
            }
            const list = getFilteredPrograms();
            if (list.length === 0) {
                emptyState?.classList.remove('hidden');
                return;
            }
            emptyState?.classList.add('hidden');
            list.forEach((program) => {
                const card = template.content.cloneNode(true);
                setTextContent(card, ".title", program.title);
                setTextContent(card, ".subtitle", program.serviceKind === RadioServiceKind.Radiko
                    ? `${program.stationName} (${program.stationId})`
                    : `${program.stationName} (${program.areaName})`);
                setTextContent(card, ".performer", program.performer ? `出演: ${program.performer}` : "");
                setTextContent(card, ".startTime", `開始時間: ${new Date(program.startTime).toLocaleString()}`);
                setTextContent(card, ".endTime", `終了時間: ${new Date(program.endTime).toLocaleString()}`);
                const descriptionHtml = sanitizeHtml(program.description ?? "");
                setInnerHtml(card, ".description", descriptionHtml);
                setupDescriptionToggle(card, descriptionHtml);
                setEventListener(card, ".play-btn", "click", () => playProgram(program.programId, program.serviceKind, program.title));
                const recordButton = card.querySelector(".record-btn");
                if (recordButton) {
                    setEventListener(card, ".record-btn", "click", () => {
                        if (isProgramEnded(program.endTime)) {
                            showToast('この番組はすでに終了しているため、リアルタイム録音できません。', false);
                            return;
                        }
                        reserveProgramByType(program.programId, program.serviceKind, RecordingType.RealTime, recordButton);
                    });
                }
                const timeFreeButton = card.querySelector(".timefree-btn");
                const isTimeFreeAvailable = program.availabilityTimeFree === AvailabilityTimeFree.Available ||
                    program.availabilityTimeFree === AvailabilityTimeFree.PartiallyAvailable;
                if (timeFreeButton) {
                    if (!isTimeFreeAvailable) {
                        timeFreeButton.classList.add('hidden');
                    }
                    else {
                        setEventListener(card, ".timefree-btn", "click", () => {
                            reserveProgramByType(program.programId, program.serviceKind, RecordingType.TimeFree, timeFreeButton);
                        });
                    }
                }
                container.appendChild(card);
            });
        };
        const setActiveAreaTab = (areaId) => {
            activeAreaId = areaId;
            activeAreaByService.set(activeServiceKind, areaId);
            render();
            buildTabs();
        };
        const setActiveServiceTab = (serviceKind) => {
            activeServiceKind = serviceKind;
            const serviceAreas = getAreasForService(serviceKind);
            const rememberedAreaId = activeAreaByService.get(serviceKind) ?? '';
            activeAreaId = serviceAreas.some(area => area.areaId === rememberedAreaId)
                ? rememberedAreaId
                : (serviceAreas[0]?.areaId ?? '');
            activeAreaByService.set(serviceKind, activeAreaId);
            render();
            buildTabs();
        };
        buildTabs();
        searchInput.addEventListener('input', () => render());
        render();
    }
    catch (error) {
        console.error('Error fetching data:', error);
    }
});
/**
 * 配信再生処理
 * @param programId
 * @param serviceKind
 */
async function playProgram(programId, serviceKind, programTitle) {
    const requestBody = {
        programId: programId,
        radioServiceKind: serviceKind
    };
    try {
        const response = await fetch(API_ENDPOINTS.PROGRAM_PLAY, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(requestBody)
        });
        if (response.ok) {
            const result = await response.json();
            const sourceToken = serviceKind === RadioServiceKind.Radiko ? result.data.token : null;
            await playHomeFromSource(result.data.url, sourceToken, programTitle ?? null);
        }
        else {
            showGlobalToast('再生に失敗しました。', false);
        }
    }
    catch (error) {
        console.error('Error:', error);
        showGlobalToast('エラーが発生しました。', false);
    }
}
async function playHomeFromSource(sourceUrl, sourceToken, programTitle) {
    await playPlayerSource({ sourceUrl, sourceToken, title: programTitle, kind: 'live' });
}
function createPlayerJumpControls(audioElm) {
    const goLiveButton = document.createElement('button');
    goLiveButton.type = 'button';
    goLiveButton.className = 'player-jump-button player-go-live-button player-icon-button';
    goLiveButton.innerHTML = '<i class="fas fa-tower-broadcast" aria-hidden="true"></i>';
    goLiveButton.setAttribute('aria-label', '現在放送中の位置へ戻る');
    goLiveButton.title = 'ライブへ戻る';
    goLiveButton.addEventListener('click', () => {
        const hls = getPlayerHls();
        const liveSyncPosition = Number(hls?.liveSyncPosition);
        if (Number.isFinite(liveSyncPosition) && liveSyncPosition > 0) {
            audioElm.currentTime = liveSyncPosition;
        }
        else if (Number.isFinite(audioElm.duration)) {
            audioElm.currentTime = audioElm.duration;
        }
        hls?.startLoad(-1);
        void resumePlayer();
    });
    return createStandardPlayerJumpControls(audioElm, {
        playbackRateOptions: playerPlaybackRateOptions,
        createSideButtons: () => [goLiveButton]
    });
}
//# sourceMappingURL=home.js.map