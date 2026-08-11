"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
var __generator = (this && this.__generator) || function (thisArg, body) {
    var _ = { label: 0, sent: function() { if (t[0] & 1) throw t[1]; return t[1]; }, trys: [], ops: [] }, f, y, t, g;
    return g = { next: verb(0), "throw": verb(1), "return": verb(2) }, typeof Symbol === "function" && (g[Symbol.iterator] = function() { return this; }), g;
    function verb(n) { return function (v) { return step([n, v]); }; }
    function step(op) {
        if (f) throw new TypeError("Generator is already executing.");
        while (g && (g = 0, op[0] && (_ = 0)), _) try {
            if (f = 1, y && (t = op[0] & 2 ? y["return"] : op[0] ? y["throw"] || ((t = y["return"]) && t.call(y), 0) : y.next) && !(t = t.call(y, op[1])).done) return t;
            if (y = 0, t) op = [op[0] & 2, t.value];
            switch (op[0]) {
                case 0: case 1: t = op; break;
                case 4: _.label++; return { value: op[1], done: false };
                case 5: _.label++; y = op[1]; op = [0]; continue;
                case 7: op = _.ops.pop(); _.trys.pop(); continue;
                default:
                    if (!(t = _.trys, t = t.length > 0 && t[t.length - 1]) && (op[0] === 6 || op[0] === 2)) { _ = 0; continue; }
                    if (op[0] === 3 && (!t || (op[1] > t[0] && op[1] < t[3]))) { _.label = op[1]; break; }
                    if (op[0] === 6 && _.label < t[1]) { _.label = t[1]; t = op; break; }
                    if (t && _.label < t[2]) { _.label = t[2]; _.ops.push(op); break; }
                    if (t[2]) _.ops.pop();
                    _.trys.pop(); continue;
            }
            op = body.call(thisArg, _);
        } catch (e) { op = [6, e]; y = 0; } finally { f = t = 0; }
        if (op[0] & 5) throw op[1]; return { value: op[0] ? op[1] : void 0, done: true };
    }
};
Object.defineProperty(exports, "__esModule", { value: true });
var accordionUtil_js_1 = require("./accordionUtil.js");
document.addEventListener('DOMContentLoaded', function () { return __awaiter(void 0, void 0, void 0, function () {
    var stationSelect, dateSelect;
    return __generator(this, function (_a) {
        switch (_a.label) {
            case 0:
                stationSelect = document.getElementById('stationSelect');
                dateSelect = document.getElementById('dateSelect');
                return [4 /*yield*/, loadStationList()];
            case 1:
                _a.sent();
                stationSelect.addEventListener('change', function () { return __awaiter(void 0, void 0, void 0, function () {
                    var stationSelect, stationId, dateSelectContainer, programList;
                    return __generator(this, function (_a) {
                        switch (_a.label) {
                            case 0:
                                stationSelect = document.getElementById('stationSelect');
                                stationId = stationSelect.value;
                                dateSelectContainer = document.getElementById('dateSelectContainer');
                                programList = document.getElementById('programList');
                                if (!stationId) return [3 /*break*/, 3];
                                dateSelectContainer.classList.remove('hidden');
                                return [4 /*yield*/, populateDateSelect()];
                            case 1:
                                _a.sent();
                                return [4 /*yield*/, loadPrograms()];
                            case 2:
                                _a.sent();
                                return [3 /*break*/, 4];
                            case 3:
                                dateSelectContainer.classList.add('hidden');
                                programList.innerHTML = '';
                                _a.label = 4;
                            case 4: return [2 /*return*/];
                        }
                    });
                }); });
                dateSelect.addEventListener('change', loadPrograms);
                return [2 /*return*/];
        }
    });
}); });
function loadStationList() {
    return __awaiter(this, void 0, void 0, function () {
        var response, stationsByRegion, stationSelect, _loop_1, region;
        return __generator(this, function (_a) {
            switch (_a.label) {
                case 0: return [4 /*yield*/, fetch('/api/Radiko/station.json')];
                case 1:
                    response = _a.sent();
                    return [4 /*yield*/, response.json()];
                case 2:
                    stationsByRegion = _a.sent();
                    stationSelect = document.getElementById('stationSelect');
                    _loop_1 = function (region) {
                        // 地域名を追加
                        var optGroup = document.createElement('optgroup');
                        optGroup.label = region;
                        stationSelect.appendChild(optGroup);
                        // 放送局を追加
                        stationsByRegion[region].forEach(function (station) {
                            var option = document.createElement('option');
                            option.value = station.stationId;
                            option.textContent = station.stationName;
                            optGroup.appendChild(option);
                        });
                    };
                    for (region in stationsByRegion) {
                        _loop_1(region);
                    }
                    return [2 /*return*/];
            }
        });
    });
}
function populateDateSelect() {
    return __awaiter(this, void 0, void 0, function () {
        var response, dates, dateSelect;
        return __generator(this, function (_a) {
            switch (_a.label) {
                case 0: return [4 /*yield*/, fetch('/api/General/radiodate.json')];
                case 1:
                    response = _a.sent();
                    return [4 /*yield*/, response.json()];
                case 2:
                    dates = _a.sent();
                    dateSelect = document.getElementById('dateSelect');
                    dates.forEach(function (dateElm) {
                        var option = document.createElement('option');
                        option.value = dateElm.value;
                        option.textContent = dateElm.textContent;
                        if (dateElm.isToday) {
                            option.selected = true;
                        }
                        dateSelect.appendChild(option);
                    });
                    return [2 /*return*/];
            }
        });
    });
}
// 番組表を取得して表示
function loadPrograms() {
    return __awaiter(this, void 0, void 0, function () {
        var stationSelect, dateSelect, stationId, date, response, programs, programList;
        return __generator(this, function (_a) {
            switch (_a.label) {
                case 0:
                    stationSelect = document.getElementById('stationSelect');
                    dateSelect = document.getElementById('dateSelect');
                    stationId = stationSelect.value;
                    date = dateSelect.value;
                    if (!(stationId && date)) return [3 /*break*/, 3];
                    return [4 /*yield*/, fetch("/api/Radiko/programlist.json?d=".concat(date, "&s=").concat(stationId))];
                case 1:
                    response = _a.sent();
                    return [4 /*yield*/, response.json()];
                case 2:
                    programs = _a.sent();
                    renderPrograms(programs);
                    return [3 /*break*/, 4];
                case 3:
                    programList = document.getElementById('programList');
                    programList.innerHTML = '';
                    _a.label = 4;
                case 4: return [2 /*return*/];
            }
        });
    });
}
function renderPrograms(programs) {
    var programList = document.getElementById('programList');
    programList.innerHTML = ''; // 現在のリストをクリア
    programs.forEach(function (program) {
        var startTime = new Date(program.start).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
        var endTime = new Date(program.end).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
        var programId = "program-".concat(program.id);
        var currentDate = new Date();
        var card = document.createElement('div');
        card.className = 'card';
        var cardHeader = document.createElement('header');
        cardHeader.className = 'card-header';
        cardHeader.onclick = function () { return (0, accordionUtil_js_1.toggleAccordion)(programId); };
        var cardHeaderTitle = document.createElement('p');
        cardHeaderTitle.className = 'card-header-title';
        cardHeaderTitle.textContent = "".concat(startTime, " \uFF5E ").concat(endTime, " : ").concat(program.title);
        cardHeader.appendChild(cardHeaderTitle);
        var cardContent = document.createElement('div');
        cardContent.id = programId;
        cardContent.className = 'card-content';
        cardContent.style.display = 'none';
        var content = document.createElement('div');
        content.className = 'content';
        var titleClm = (0, accordionUtil_js_1.createColumns)('タイトル:', program.title);
        var performerClm = (0, accordionUtil_js_1.createColumns)('出演者:', program.performer);
        var descriptionClm = (0, accordionUtil_js_1.createColumns)('説明:', program.description);
        content.appendChild(titleClm);
        content.appendChild(performerClm);
        content.appendChild(descriptionClm);
        var buttonColumns = document.createElement('div');
        buttonColumns.className = 'columns is-mobile';
        if (program.availabilityTimeFree === 0 || program.availabilityTimeFree === 1) {
            var timeFreeBtnClm = document.createElement('div');
            timeFreeBtnClm.className = 'column';
            var timeFreeBtn = document.createElement('button');
            timeFreeBtn.className = 'button is-primary';
            timeFreeBtn.textContent = 'タイムフリー録音';
            timeFreeBtn.onclick = function () { return (0, accordionUtil_js_1.recordProgram)(program.id, 2); };
            timeFreeBtnClm.appendChild(timeFreeBtn);
            buttonColumns.appendChild(timeFreeBtnClm);
        }
        if (currentDate < new Date(program.end)) {
            var realTimeRecBtnClm = document.createElement('div');
            realTimeRecBtnClm.className = 'column';
            var realTimeRecBtn = document.createElement('button');
            realTimeRecBtn.className = 'button is-primary';
            realTimeRecBtn.textContent = 'リアルタイム録音';
            realTimeRecBtn.onclick = function () { return (0, accordionUtil_js_1.recordProgram)(program.id, 1); };
            realTimeRecBtnClm.appendChild(realTimeRecBtn);
            buttonColumns.appendChild(realTimeRecBtnClm);
        }
        content.appendChild(buttonColumns);
        cardContent.appendChild(content);
        card.appendChild(cardHeader);
        card.appendChild(cardContent);
        programList.appendChild(card);
    });
}
//# sourceMappingURL=radikoprogram.js.map