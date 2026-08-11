"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
var accordionUtil_js_1 = require("./accordionUtil.js");
document.querySelectorAll('input[name="RadioService"]').forEach(function (radio) {
    radio.addEventListener('change', function () {
        var service = this.value;
        if (service === 'Radiko') {
            document.getElementById('stationForm').style.display = 'block';
            fetch('/api/Radiko/station.json')
                .then(function (response) { return response.json(); })
                .then(function (data) {
                var stationGroups = document.getElementById('stationGroups');
                stationGroups.innerHTML = '';
                Object.keys(data).forEach(function (region) {
                    var regionDiv = document.createElement('div');
                    regionDiv.className = 'region';
                    var regionHeader = document.createElement('div');
                    regionHeader.className = 'field';
                    regionHeader.innerHTML = "\n                            <label class=\"label\">".concat(region, " <input type=\"checkbox\" class=\"region-checkbox\" checked> \u3053\u306E\u5730\u57DF\u306E\u653E\u9001\u5C40\u3092\u3059\u3079\u3066\u30C1\u30A7\u30C3\u30AF</label>\n                        ");
                    regionDiv.appendChild(regionHeader);
                    var stationsDiv = document.createElement('div');
                    stationsDiv.className = 'columns is-multiline';
                    data[region].forEach(function (station) {
                        var label = document.createElement('label');
                        label.className = 'column is-one-third';
                        label.innerHTML = "\n                                <input type=\"checkbox\" name=\"SelectedStationIds\" value=\"".concat(station.stationId, "\" checked>\n                                ").concat(station.stationName, "\n                            ");
                        stationsDiv.appendChild(label);
                    });
                    regionDiv.appendChild(stationsDiv);
                    stationGroups.appendChild(regionDiv);
                    var regionCheckbox = regionHeader.querySelector('.region-checkbox');
                    regionCheckbox.addEventListener('change', function () {
                        var _this = this;
                        var checkboxes = stationsDiv.querySelectorAll('input[type="checkbox"]');
                        checkboxes.forEach(function (checkbox) {
                            checkbox.checked = _this.checked;
                        });
                    });
                    stationsDiv.querySelectorAll('input[type="checkbox"]').forEach(function (checkbox) {
                        checkbox.addEventListener('change', function () {
                            var allChecked = Array.from(stationsDiv.querySelectorAll('input[type="checkbox"]')).every(function (cb) { return cb.checked; });
                            regionCheckbox.checked = allChecked;
                        });
                    });
                });
            });
        }
        else {
            document.getElementById('stationForm').style.display = 'none';
        }
    });
});
document.getElementById('searchButton').addEventListener('click', function () {
    var selectedStationIds = Array.from(document.querySelectorAll('input[name="SelectedStationIds"]:checked')).map(function (checkbox) { return checkbox.value; });
    var keyword = document.getElementById('Keyword').value;
    var searchTitleOnly = document.getElementById('SearchTitleOnly').checked;
    var selectedDaysOfWeek = Array.from(document.querySelectorAll('input[name="SelectedDaysOfWeek"]:checked')).map(function (checkbox) { return parseInt(checkbox.value); });
    var data = {
        SelectedStationIds: selectedStationIds,
        Keyword: keyword,
        SearchTitleOnly: searchTitleOnly,
        SelectedDaysOfWeek: selectedDaysOfWeek
    };
    fetch('/api/Program/search.json', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(data)
    })
        .then(function (response) { return response.json(); })
        .then(function (programs) {
        var searchResultElm = document.getElementById('searchResult');
        searchResultElm.innerHTML = ''; // 現在のリストをクリア
        if (programs && programs.length > 0) {
            var searchResultTitle = document.createElement('h2');
            searchResultTitle.className = 'subtitle';
            searchResultTitle.textContent = '検索結果';
            searchResultElm.appendChild(searchResultTitle);
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
                cardHeaderTitle.innerHTML = "".concat(startTime, " \uFF5E ").concat(endTime, " [").concat(program.stationId, "] ").concat(program.title, "<br />\u3000\u51FA\u6F14\u8005\uFF1A").concat(program.performer);
                cardHeader.appendChild(cardHeaderTitle);
                var cardContent = document.createElement('div');
                cardContent.id = programId;
                cardContent.className = 'card-content';
                cardContent.style.display = 'none';
                var content = document.createElement('div');
                content.className = 'content';
                var descriptionClm = (0, accordionUtil_js_1.createColumns)('説明:', program.description);
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
                searchResultElm.appendChild(card);
            });
        }
        else {
            var searchResultTitle = document.createElement('p');
            searchResultTitle.textContent = '該当する番組が見つかりませんでした。';
            searchResultElm.appendChild(searchResultTitle);
        }
    });
});
//# sourceMappingURL=search.js.map