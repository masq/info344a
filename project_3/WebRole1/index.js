var updateInterval = null;

(function () {
    "use strict";

    var crawling = false;
    var started = false;

    var states = ["INIT", "LOADING", "RUNNING", "IDLE"];

    window.onload = function () {
        document.getElementById("pause").onclick = toggleCrawling;
        document.getElementById("stop").onclick = startStopCrawling;
        // TODO:
        document.getElementById("test").onclick = getInfoOnURL;
        // TODO: 
        for (var tabs of document.querySelectorAll("tabs")) { // there should only be one instance of tabs
            tabs.onclick = switchTabs;
        }

        updateInterval = setInterval(getRecentStats, 10000);
        getRecentStats(); // fetch data to fill page
    };

    function getInfoOnURL(event) {
        //http://www.cnn.com/2016/02/04/us/beyond-the-border-life-in-limbo/index.html
        console.log("[*] Getting info...");
        var op = "GetInfoOnURL";
        var url = document.getElementById("search").value.trim();
        if (url) {
            var data = {
                "url": url
            }
            apiRequest(op, JSON.stringify(data))
                .then(displayURLInfo, errorOut.bind(null, "getInfoOnURL"));
        } else {
            return false;
        }
    }

    function displayURLInfo(resp) {
        try {
            var jason = JSON.parse(resp);
            if (jason.d) {
                var info = jason.d;
                var title = info[0];
                var ts = info[1];
                var url = info[2];
                alert("url: " + url + " => title: " + title);
            }
        } catch (e) {
            errorOut("displayURLInfo", e, resp);
        }
    }

    function getRecentStats() {
        console.log("[*] Updating Stats...");
        var op = "getRecentStats";
        apiRequest(op, JSON.stringify({}))
            .then(updateStats, errorOut.bind(null, "getRecentStats"));
    }

    function updateStats(resp) {
        try {
            var jason = JSON.parse(resp);
            if (jason.d && jason.d.length > 0) {
                var stats = jason.d;
                var stat = stats[stats.length - 1]; // get latest stat

                var cpu = stat.cpu;
                var ram = stat.ram;
                var qs = stat.queueSize;
                var uc = stat.urlCount;
                var recents = stat.recents.split(";;;;;");
                var crawlStates = stat.crawlerStates;
                var crawlers = document.getElementById("crawlers");
                crawlers.innerHTML = "";
                for (var state in crawlStates) {
                    crawlers.appendChild(crawlerFactory(states[crawlStates[state]]));
                }
                var rTableBody = document.getElementById("recentsTable").querySelector("tbody");
                rTableBody.innerHTML = "";
                for (var recent in recents) {
                    if (recents[recent]) {
                        rTableBody.appendChild(createTableEntry(recents[recent], recent));
                    }
                }
                var eTableBody = document.getElementById("errorsTable").querySelector("tbody");
                var errors = stat.errors.split(";;;;;")
                for (var air in errors) {
                    if (errors[air]) {
                        eTableBody.appendChild(createTableEntry(errors[air], air));
                    }
                }
                document.getElementById("queueSize").textContent = qs;
                document.getElementById("tableSize").textContent = uc;
                document.getElementById("cpuUtil").textContent = cpu;
                document.getElementById("ramAvail").textContent = ram;
                console.log(jason.d);
            }
        } catch(e) {
            errorOut("updateStats", e, resp);
        }
    }

    function createTableEntry(rowData, index) {
        var rowData = rowData.split("|||||");
        var row = document.createElement("tr");
        var num = document.createElement("td");
        var url = document.createElement("td");
        var title = document.createElement("td");
        var ts = document.createElement("td");
        num.appendChild(document.createTextNode(index));
        url.appendChild(document.createTextNode(rowData[0]));
        title.appendChild(document.createTextNode(rowData[1]));
        ts.appendChild(document.createTextNode(rowData[2]));
        row.appendChild(num);
        row.appendChild(url);
        row.appendChild(title);
        row.appendChild(ts);
        return row;
    }

    function crawlerFactory(state) {
        var c = document.createElement("crawler");
        var status = document.createElement("p");
        var pause = document.createElement("button");
        var stop = document.createElement("button");
        var spanPause = pause.appendChild(document.createElement("span"));
        var spanStop = stop.appendChild(document.createElement("span"));
        pause.setAttribute("type", "button");
        pause.classList.add("btn", "btn-default");
        stop.setAttribute("type", "button");
        stop.classList.add("btn", "btn-danger");
        // TODO: add event handlers on these.
        spanPause.classList.add("glyphicon", "glyphicon-pause");
        spanStop.classList.add("glyphicon", "glyphicon-off");

        status.appendChild(document.createTextNode(state));
        status.classList.add("lead");
        c.appendChild(status);
        c.appendChild(pause);
        c.appendChild(stop);
        return c;
    }

    function startStopCrawling(event) {
        var oPower = event.target;
        var op = "CrawlerCommand";
        var data = {};
        if (started) {
            data.cmd = "stop";
            document.getElementById("pause").disabled = true;
        } else {
            data.cmd = "start";
            document.getElementById("pause").disabled = false;
        }
        apiRequest(op, JSON.stringify(data))
            .then(function (resp) {
                console.log("[*] crawl toggle response: ", resp);
                try {
                    var jason = JSON.parse(resp);
                    if (jason.d) {
                        crawling = !crawling;
                        started = !started;
                    }
                } catch (e) {
                    errorOut("startStopCrawling", e, resp);
                }
            }, errorOut.bind(null, "startStopCrawling"));
    }

    function toggleCrawling(event) {
        var oPause = event.target;
        var icon = oPause.querySelector("span.glyphicon");
        var op = "CrawlerCommand";
        var data = {};
        if (crawling) {
            // remove the pause icon, put the play icon here
            // since we just pushed it when it had the pause icon.
            icon.classList.remove("glyphicon-pause");
            icon.classList.add("glyphicon-play");
            data.cmd = "pause";
        } else { // just hit play since it was not currently crawling
            // so remove the play icon, and put the pause one
            icon.classList.remove("glyphicon-play");
            icon.classList.add("glyphicon-pause");
            data.cmd = "resume";
        }
        apiRequest(op, JSON.stringify(data))
            .then(function (resp) {
                console.log("[*] crawl toggle response: ", resp);
                try {
                    var jason = JSON.parse(resp);
                    if (jason.d) {
                        crawling = !crawling;
                    }
                } catch (e) {
                    errorOut("startStopCrawling", e, resp);
                }
            }, errorOut.bind(null, "toggleCrawling"));
    }

    // string op to perform
    // JSON string params to send
    function apiRequest(op, params) {
        return new Promise(function (resolve, reject) {
            var xhr = new XMLHttpRequest();
            xhr.open("POST", "/Admin.asmx/" + op);
            xhr.setRequestHeader("Content-Type", "application/json; charset=utf-8");
            xhr.onload = function () { resolve(xhr.responseText); };
            xhr.onerror = function () { reject(xhr, xhr.responseText, arguments); };
            xhr.send(params);
        });
    }

    function switchTabs(event) {
        if (event.target.tagName === "TAB") {
            // remove active from current active tab, give it to the one just clicked on.
            event.target.parentElement.querySelector("tab.active").classList.remove("active");
            event.target.classList.add("active");
            // switch the active view to the view corresponding to the tab clicked on.
            document.querySelector(".output > *.active").classList.remove("active");
            document.querySelector(event.target.getAttribute("data-target")).classList.add("active");
        }
    }

    function errorOut(from) {
        console.error("[-] Error in", from, "function");
        var first = true;
        for (var arg of arguments) {
            if (first) first = false;
            else console.error("\t[-] ", arg);
        }
    }


})();