import { StationDataResponseContract as StationData } from './openapi-response-contract.js';
import { createCard } from './cardUtil.js';
import { API_ENDPOINTS } from './const.js';
import { RadioServiceKind, RadioServiceKindMap } from './define.js';



export async function generateStationList(kind: RadioServiceKind, checkStationList?: string[]): Promise<HTMLElement> {

    const stationList = await getStationList(kind);

    return renderStationList(stationList, kind, checkStationList);
}


async function getStationList(kind: RadioServiceKind): Promise<StationData> {

    switch (kind) {
        case RadioServiceKind.Radiko:
            return fetch(API_ENDPOINTS.STATION_LIST_RADIKO)
                .then(response => response.json())
                .then(result => {
                    const source = result.data as Record<string, Array<{
                        stationId?: string;
                        stationName?: string;
                        regionId?: string;
                        regionName?: string;
                    }>>;
                    const normalized: StationData = {};
                    Object.keys(source ?? {}).forEach((regionName) => {
                        normalized[regionName] = (source[regionName] ?? []).map((x) => ({
                            areaId: x.regionId ?? '',
                            areaName: x.regionName ?? regionName,
                            stationId: x.stationId ?? '',
                            stationName: x.stationName ?? ''
                        }));
                    });
                    return normalized;
                });

        case RadioServiceKind.Radiru:
            return fetch(API_ENDPOINTS.STATION_LIST_RADIRU)
                .then(response => response.json())
                .then(result => {
                    const source = result.data as Record<string, Array<{
                        stationId?: string;
                        stationName?: string;
                        areaId?: string;
                        areaName?: string;
                    }>>;
                    const normalized: StationData = {};
                    Object.keys(source ?? {}).forEach((areaName) => {
                        normalized[areaName] = (source[areaName] ?? []).map((x) => ({
                            areaId: x.areaId ?? '',
                            areaName: x.areaName ?? areaName,
                            stationId: x.stationId ?? '',
                            stationName: x.stationName ?? ''
                        }));
                    });
                    return normalized;
                });

        default:
            throw new Error(`Unsupported RadioServiceKind: ${kind}`);
    }
}



function renderStationList(data: StationData, kind: RadioServiceKind, checkStationList?: string[]): HTMLElement {

    const contentElements: Array<HTMLElement> = [];

    Object.keys(data).forEach(region => {
        const regionDiv = document.createElement('div');
        regionDiv.className = 'region pb-6';

        const regionHeader = document.createElement('div');
        regionHeader.className = 'field';

        const regionHeaderTitle = document.createElement('p');
        regionHeaderTitle.className = 'title is-4 mb-3';
        regionHeaderTitle.textContent = region;
        regionHeader.appendChild(regionHeaderTitle);

        const regionCheckBoxLabel = document.createElement('label');
        regionCheckBoxLabel.className = 'label';

        const regionCheckBox = document.createElement('input');
        regionCheckBox.type = 'checkbox';
        regionCheckBox.className = 'region-checkbox';
        regionCheckBox.checked = true;
        regionCheckBoxLabel.appendChild(regionCheckBox);
        regionCheckBoxLabel.appendChild(document.createTextNode(' この地域の放送局をすべてチェック'));

        regionHeader.appendChild(regionCheckBoxLabel);

        regionDiv.appendChild(regionHeader);

        const stationsDiv = document.createElement('div');
        stationsDiv.className = 'columns is-multiline';
        data[region].forEach(station => {
            const label = document.createElement('label');
            label.className = 'column is-one-third';

            const stationCheckBox = document.createElement('input');
            stationCheckBox.type = 'checkbox';

            switch (kind) {
                case RadioServiceKind.Radiko:
                    stationCheckBox.name = 'SelectedRadikoStationIds';
                    stationCheckBox.value = `${station.stationId}`;
                    break;

                case RadioServiceKind.Radiru:
                    stationCheckBox.name = 'SelectedRadiruStationIds';
                    stationCheckBox.value = `${station.areaId}:${station.stationId}`;
                    break;

                default:
                    throw new Error(`Unsupported RadioServiceKind: ${kind}`);
            }

            if (checkStationList === undefined || (checkStationList && checkStationList.includes(stationCheckBox.value))) {
                stationCheckBox.checked = true;
            }

            label.appendChild(stationCheckBox);
            label.appendChild(document.createTextNode(station.stationName));

            stationsDiv.appendChild(label);
        });

        regionDiv.appendChild(stationsDiv);
        contentElements.push(regionDiv);

        const regionCheckbox = regionHeader.querySelector<HTMLInputElement>('.region-checkbox')!;
        regionCheckbox.addEventListener('change', function (this: HTMLInputElement) {
            const checkboxes = stationsDiv.querySelectorAll<HTMLInputElement>('input[type="checkbox"]');
            checkboxes.forEach(checkbox => {
                checkbox.checked = this.checked;
            });
        });

        stationsDiv.querySelectorAll<HTMLInputElement>('input[type="checkbox"]').forEach(checkbox => {
            checkbox.addEventListener('change', function (this: HTMLInputElement) {
                const allChecked = Array.from(stationsDiv.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')).every(cb => cb.checked);
                regionCheckbox.checked = allChecked;
            });
        });
    });

    const cardHeaderTitle = document.createElement('p');

    cardHeaderTitle.textContent = `${RadioServiceKindMap[kind].displayName}放送局リスト`;

    const cardElm = createCard(`${RadioServiceKindMap[kind].codeId}stationList`, cardHeaderTitle, contentElements);

    return cardElm;
}


