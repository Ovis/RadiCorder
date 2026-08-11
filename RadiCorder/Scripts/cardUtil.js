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
exports.closeModal = exports.showModal = exports.recordProgram = exports.toggleAccordion = exports.createColumns = void 0;
function createColumns(label, value) {
    var columns = document.createElement('div');
    columns.className = 'columns';
    var labelColumn = document.createElement('div');
    labelColumn.className = 'column is-one-quarter';
    labelColumn.innerHTML = "<strong>".concat(label, "</strong>");
    var valueColumn = document.createElement('div');
    valueColumn.className = 'column';
    valueColumn.innerHTML = value;
    columns.appendChild(labelColumn);
    columns.appendChild(valueColumn);
    return columns;
}
exports.createColumns = createColumns;
function toggleAccordion(id) {
    var element = document.getElementById(id);
    if (element.style.display === 'none' || element.style.display === '') {
        element.style.display = 'block';
    }
    else {
        element.style.display = 'none';
    }
}
exports.toggleAccordion = toggleAccordion;
function recordProgram(programId, type) {
    return __awaiter(this, void 0, void 0, function () {
        var response, error_1;
        return __generator(this, function (_a) {
            switch (_a.label) {
                case 0:
                    _a.trys.push([0, 2, , 3]);
                    return [4 /*yield*/, fetch('/api/Radiko/recordingprogram', {
                            method: 'POST',
                            headers: {
                                'Content-Type': 'application/json'
                            },
                            body: JSON.stringify({ programId: programId, recordingType: type })
                        })];
                case 1:
                    response = _a.sent();
                    if (response.ok) {
                        showModal('録音予約を開始しました');
                    }
                    else {
                        showModal('録音予約に失敗しました');
                    }
                    return [3 /*break*/, 3];
                case 2:
                    error_1 = _a.sent();
                    console.error('Error:', error_1);
                    showModal('録音予約に失敗しました');
                    return [3 /*break*/, 3];
                case 3: return [2 /*return*/];
            }
        });
    });
}
exports.recordProgram = recordProgram;
function showModal(message) {
    var modalMessage = document.getElementById('modalMessage');
    modalMessage.textContent = message;
    var modal = document.getElementById('recordingModal');
    modal.classList.add('is-active');
    setTimeout(closeModal, 5000);
}
exports.showModal = showModal;
function closeModal() {
    var modal = document.getElementById('recordingModal');
    modal.classList.remove('is-active');
}
exports.closeModal = closeModal;
//# sourceMappingURL=accordionUtil.js.map