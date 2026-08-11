import { RadioServiceKind, RadioServiceKindMap } from './define.js';
import { RecordingType } from './define.js';
import { AvailabilityTimeFree } from './define.js';
import type {
    ApiResponseContract,
    ProgramPlaybackInfoResponseContract,
    ProgramAreaResponseContract,
    ProgramNowOnAirResponseContract,
    RadioProgramResponseContract as Program
} from './openapi-response-contract.js';
import { API_ENDPOINTS } from './const.js';
import type { ProgramInformationRequestContract } from './openapi-contract.js';
import { showGlobalToast } from './feedback.js';
import { setTextContent, setInnerHtml, setEventListener, sanitizeHtml } from './utils.js';
import { playerPlaybackRateOptions, applyPlaybackRate } from './player-rate-control.js';
import { createStandardPlayerJumpControls } from './player-jump-controls.js';
import { readPersistedPlayerState, writePersistedPlayerState, clearPersistedPlayerState } from './player-state-store.js';
import type { HlsLiveInstance, HlsWindow } from './hls-types.js';

let activeGoLiveAction: (() => void) | null = null;
const defaultDocumentTitle = document.title;
let currentPlayingSourceUrl: string | null = null;
let currentPlayingSourceToken: string | null = null;
let currentPlayingProgramTitle: string | null = null;

function updateHomeDocumentTitle(programTitle: string | null): void {
    const title = programTitle?.trim();
    if (!title) {
        document.title = defaultDocumentTitle;
        return;
    }

    document.title = `${title} - RadiCorder`;
}

function persistCurrentPlaybackState(): void {
    if (!currentPlayingSourceUrl) {
        return;
    }

    const audio = document.getElementById('audio-player-elm') as HTMLAudioElement | null;
    writePersistedPlayerState({
        sourceUrl: currentPlayingSourceUrl,
        sourceToken: currentPlayingSourceToken,
        title: currentPlayingProgramTitle,
        currentTime: audio ? audio.currentTime : 0,
        playbackRate: audio ? audio.playbackRate : 1,
        wasPlaying: audio ? !audio.paused && !audio.ended : true,
        savedAtUtc: new Date().toISOString()
    });
}

document.addEventListener('DOMContentLoaded', async () => {
    try {
        const response = await fetch(API_ENDPOINTS.PROGRAM_NOW);
        const result = await response.json() as ApiResponseContract<ProgramNowOnAirResponseContract>;
        const data = result.data;

        const template = document.getElementById('program-card-template') as HTMLTemplateElement;
        const container = document.getElementById('programs-container') as HTMLDivElement;
        const emptyState = document.getElementById('programs-empty') as HTMLDivElement;
        const tabsWrapper = document.getElementById('area-tabs-wrapper') as HTMLDivElement;
        const tabsContainer = document.getElementById('area-tabs') as HTMLDivElement;
        const searchInput = document.getElementById('now-search') as HTMLInputElement;
        const resultToast = document.getElementById('home-result-toast') as HTMLDivElement | null;
        const resultToastMessage = document.getElementById('home-result-toast-message') as HTMLSpanElement | null;
        const resultToastClose = document.getElementById('home-result-toast-close') as HTMLButtonElement | null;

        let toastTimerId: number | undefined;
        const reservedRecordingKeys = new Set<string>();

        const showToast = (message: string, isSuccess = true) => {
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

        const reserveProgramByType = async (
            programId: string,
            serviceKind: RadioServiceKind,
            recordingType: RecordingType,
            button: HTMLElement) => {
            const reservationKey = `${programId}:${recordingType}`;
            if (reservedRecordingKeys.has(reservationKey)) {
                return;
            }

            button.classList.add('is-static');
            button.classList.add('opacity-70');

            try {
                const requestBody: ProgramInformationRequestContract = {
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

                const result = await response.json() as ApiResponseContract<null>;

                if (response.ok && result.success) {
                    reservedRecordingKeys.add(reservationKey);
                    button.classList.add('pointer-events-none');
                    button.textContent = '予約済み';
                    showToast(result.message ?? '録音予約を開始しました。', true);
                    return;
                }

                showToast(result?.message ?? '録音予約に失敗しました。', false);
            } catch (error) {
                console.error('Error:', error);
                showToast('録音予約に失敗しました。', false);
            }

            button.classList.remove('is-static');
            button.classList.remove('opacity-70');
        };

        const setupDescriptionToggle = (card: HTMLElement, descriptionHtml: string) => {
            const descriptionElement = card.querySelector(".description") as HTMLElement | null;
            const toggleElement = card.querySelector(".description-toggle") as HTMLButtonElement | null;
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

        const isProgramEnded = (endTime: string): boolean => {
            return new Date(endTime).getTime() <= Date.now();
        };

        let programs: Program[] = [];
        let areas: ProgramAreaResponseContract[] = [];
        let currentAreaStations: string[] = [];
        let activeServiceKind: RadioServiceKind = RadioServiceKind.Undefined;
        let activeAreaId = '';
        const activeAreaByService = new Map<RadioServiceKind, string>();

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

            const dedup = new Map<string, string>();
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
        const areaOrderMap = new Map<string, number>(areas.map(area => [area.areaId, area.areaOrder]));

        const serviceKinds = [RadioServiceKind.Radiko, RadioServiceKind.Radiru]
            .filter(serviceKind => programs.some(program => program.serviceKind === serviceKind));

        const getAreasForService = (serviceKind: RadioServiceKind) => {
            const dedup = new Map<string, string>();
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
                list = list.filter(p =>
                    (p.title ?? '').toLowerCase().includes(query) ||
                    (p.stationName ?? '').toLowerCase().includes(query)
                );
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
                const card = template.content.cloneNode(true) as HTMLElement;

                setTextContent(card, ".title", program.title);
                setTextContent(
                    card,
                    ".subtitle",
                    program.serviceKind === RadioServiceKind.Radiko
                        ? `${program.stationName} (${program.stationId})`
                        : `${program.stationName} (${program.areaName})`);
                setTextContent(card, ".performer", program.performer ? `出演: ${program.performer}` : "");
                setTextContent(card, ".startTime", `開始時間: ${new Date(program.startTime).toLocaleString()}`);
                setTextContent(card, ".endTime", `終了時間: ${new Date(program.endTime).toLocaleString()}`);
                const descriptionHtml = sanitizeHtml(program.description ?? "");
                setInnerHtml(card, ".description", descriptionHtml);
                setupDescriptionToggle(card, descriptionHtml);
                setEventListener(card, ".play-btn", "click", () => playProgram(program.programId, program.serviceKind, program.title));
                const recordButton = card.querySelector(".record-btn") as HTMLElement | null;
                if (recordButton) {
                    setEventListener(card, ".record-btn", "click", () => {
                        if (isProgramEnded(program.endTime)) {
                            showToast('この番組はすでに終了しているため、リアルタイム録音できません。', false);
                            return;
                        }
                        reserveProgramByType(program.programId, program.serviceKind, RecordingType.RealTime, recordButton);
                    });
                }

                const timeFreeButton = card.querySelector(".timefree-btn") as HTMLElement | null;
                const isTimeFreeAvailable =
                    program.availabilityTimeFree === AvailabilityTimeFree.Available ||
                    program.availabilityTimeFree === AvailabilityTimeFree.PartiallyAvailable;

                if (timeFreeButton) {
                    if (!isTimeFreeAvailable) {
                        timeFreeButton.classList.add('hidden');
                    } else {
                        setEventListener(card, ".timefree-btn", "click", () => {
                            reserveProgramByType(program.programId, program.serviceKind, RecordingType.TimeFree, timeFreeButton);
                        });
                    }
                }

                container.appendChild(card);
            });
        };

        const setActiveAreaTab = (areaId: string) => {
            activeAreaId = areaId;
            activeAreaByService.set(activeServiceKind, areaId);
            render();
            buildTabs();
        };

        const setActiveServiceTab = (serviceKind: RadioServiceKind) => {
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

        window.addEventListener('beforeunload', () => {
            persistCurrentPlaybackState();
        });

        await tryResumePersistedPlayback();
    } catch (error) {
        console.error('Error fetching data:', error);
    }
});

/**
 * 配信再生処理
 * @param programId
 * @param serviceKind
 */
async function playProgram(programId: string, serviceKind: RadioServiceKind, programTitle?: string): Promise<void> {
    const requestBody: ProgramInformationRequestContract = {
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
            const result = await response.json() as ApiResponseContract<ProgramPlaybackInfoResponseContract>;
            const sourceToken = serviceKind === RadioServiceKind.Radiko ? result.data.token : null;
            await playHomeFromSource(result.data.url, sourceToken, programTitle ?? null, 0, playerPlaybackRateOptions[0]);
        } else {
            activeGoLiveAction = null;
            showGlobalToast('再生に失敗しました。', false);
        }
    } catch (error) {
        activeGoLiveAction = null;
        console.error('Error:', error);
        showGlobalToast('エラーが発生しました。', false);
    }
}

async function playHomeFromSource(
    sourceUrl: string,
    sourceToken: string | null,
    programTitle: string | null,
    startTimeSeconds: number,
    playbackRate: number,
    options: { isRestore?: boolean } = {}): Promise<void> {
    const footer: HTMLElement = document.getElementById('audio-player') as HTMLElement;
    let audio: HTMLAudioElement | null = document.getElementById('audio-player-elm') as HTMLAudioElement;

    if (!audio) {
        footer.innerHTML = "";
        const playerContainerElm: HTMLDivElement = document.createElement('div');
        playerContainerElm.className = 'player-container';
        const playerMainRowElm: HTMLDivElement = document.createElement('div');
        playerMainRowElm.className = 'player-main-row';

        const audioPlayerElm: HTMLAudioElement = document.createElement('audio');
        audioPlayerElm.id = 'audio-player-elm';
        audioPlayerElm.style.width = "100%";
        audioPlayerElm.style.height = "2rem";
        audioPlayerElm.controls = true;

        const closeButton = document.createElement('button');
        closeButton.type = 'button';
        closeButton.className = 'player-close-button';
        closeButton.setAttribute('aria-label', 'プレイヤーを閉じる');
        closeButton.innerHTML = '<i class="fas fa-xmark" aria-hidden="true"></i>';
        closeButton.addEventListener('click', () => {
            const player = document.getElementById('audio-player-elm') as HTMLAudioElement | null;
            if (player) {
                player.pause();
                player.removeAttribute('src');
                player.load();
            }
            activeGoLiveAction = null;
            currentPlayingSourceUrl = null;
            currentPlayingSourceToken = null;
            currentPlayingProgramTitle = null;
            clearPersistedPlayerState();
            updateHomeDocumentTitle(null);
            footer.innerHTML = '';
        });

        playerMainRowElm.appendChild(audioPlayerElm);
        playerMainRowElm.appendChild(closeButton);
        playerContainerElm.appendChild(playerMainRowElm);
        playerContainerElm.appendChild(createPlayerJumpControls(audioPlayerElm));
        footer.appendChild(playerContainerElm);

        audio = document.getElementById('audio-player-elm') as HTMLAudioElement;
    }

    const previousSourceUrl = currentPlayingSourceUrl;
    const previousSourceToken = currentPlayingSourceToken;
    currentPlayingSourceUrl = sourceUrl;
    currentPlayingSourceToken = sourceToken;
    currentPlayingProgramTitle = programTitle;
    updateHomeDocumentTitle(programTitle);
    const isSameSource =
        previousSourceUrl === sourceUrl &&
        (previousSourceToken ?? '') === (sourceToken ?? '');
    const effectivePlaybackRate = options.isRestore
        ? playbackRate
        : (isSameSource ? playbackRate : playerPlaybackRateOptions[0]);

    const hlsConstructor = (window as HlsWindow<HlsLiveInstance>).Hls;
    if (hlsConstructor?.isSupported?.()) {
        const hls = new hlsConstructor();
        applyPlaybackRate(audio!, effectivePlaybackRate);

        if (sourceToken) {
            hls.config.xhrSetup = function (xhr: XMLHttpRequest) {
                xhr.setRequestHeader('X-Radiko-AuthToken', sourceToken);
            };
        }

        hls.loadSource(sourceUrl);
        hls.attachMedia(audio);
        hls.on(hlsConstructor.Events.MANIFEST_PARSED, () => {
            if (startTimeSeconds > 0) {
                audio!.currentTime = startTimeSeconds;
            }
            audio!.play();
        });
        activeGoLiveAction = () => {
            const liveSyncPosition = Number(hls.liveSyncPosition);
            if (Number.isFinite(liveSyncPosition) && liveSyncPosition > 0) {
                audio!.currentTime = liveSyncPosition;
            } else if (Number.isFinite(audio!.duration)) {
                audio!.currentTime = audio!.duration;
            }
            hls.startLoad(-1);
            void audio!.play();
        };
    } else if (audio.canPlayType('application/vnd.apple.mpegurl')) {
        applyPlaybackRate(audio!, effectivePlaybackRate);
        audio.src = sourceUrl;
        audio.onloadedmetadata = () => {
            if (startTimeSeconds > 0) {
                audio!.currentTime = startTimeSeconds;
            }
            void audio!.play();
        };
        activeGoLiveAction = () => {
            if (Number.isFinite(audio!.duration)) {
                audio!.currentTime = audio!.duration;
            }
            void audio!.play();
        };
    } else {
        activeGoLiveAction = null;
        showGlobalToast('このブラウザはHLS再生に対応していません。', false);
        return;
    }

    persistCurrentPlaybackState();
}

async function tryResumePersistedPlayback(): Promise<void> {
    if (document.getElementById('audio-player-elm')) {
        return;
    }

    const state = readPersistedPlayerState();
    if (!state) {
        return;
    }

    const savedAt = new Date(state.savedAtUtc).getTime();
    if (!Number.isFinite(savedAt)) {
        clearPersistedPlayerState();
        return;
    }

    // 直近15分以内のみ復帰
    if (Date.now() - savedAt > 15 * 60 * 1000) {
        clearPersistedPlayerState();
        return;
    }

    await playHomeFromSource(
        state.sourceUrl,
        state.sourceToken ?? null,
        state.title ?? null,
        state.currentTime ?? 0,
        state.playbackRate ?? playerPlaybackRateOptions[0],
        { isRestore: true });
}

function createPlayerJumpControls(audioElm: HTMLAudioElement): HTMLDivElement {
    const goLiveButton = document.createElement('button');
    goLiveButton.type = 'button';
    goLiveButton.className = 'player-jump-button player-go-live-button player-icon-button';
    goLiveButton.innerHTML = '<i class="fas fa-tower-broadcast" aria-hidden="true"></i>';
    goLiveButton.setAttribute('aria-label', '現在放送中の位置へ戻る');
    goLiveButton.title = 'ライブへ戻る';

    goLiveButton.addEventListener('click', () => {
        if (!activeGoLiveAction) {
            return;
        }

        activeGoLiveAction();
    });

    return createStandardPlayerJumpControls(audioElm, {
        playbackRateOptions: playerPlaybackRateOptions,
        createSideButtons: () => [goLiveButton]
    });
}


