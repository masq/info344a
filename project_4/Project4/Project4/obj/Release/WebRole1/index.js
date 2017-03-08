(function () {
    "use strict";
    var prev = "";
    var value_delimiter = "|||||";
    var row_delimiter = ";;;;;";

    window.onload = function () {
        var sb = document.getElementById("searchBox");
        var tb = document.getElementById("searchButton");
        var fm = document.getElementById("searchForm");
        fm.onsubmit = function (event) {
            event.preventDefault();
            var pageNum = 0; // since it is the first main query
            search(sb.value, pageNum).then(displaySearchResults.bind(null, sb.value, pageNum), errorOut);
            getNBAData(sb.value.trim()).then(displayNBAStats, errorOut);
            document.querySelector("header").classList.add("hidden");
            document.querySelector(".chitikaAdContainer").classList.add("active");
            document.querySelector("main").classList.add("active");
            return false;
        }
        var o = document.querySelector("output");
        tb.onclick = function (event) {
            query({ "target": sb }).then(displayResults.bind(null, sb.value.trim()), errorOut);

        };
        sb.onkeyup = function (event) {
            if (/^[a-z\d ]+$/i.test(event.which)) {
                if (sb.value.trim()) {
                    query(event).then(displayResults.bind(null, sb.value.trim()), errorOut);
                }
            }
            if (sb.value === "") {
                outputReset();
            }
        };
        document.body.onclick = outputReset;
    };

    function displaySearchResults(searchParam, pageNum, resp) {
        try {
            var jason = JSON.parse(resp);
            var resultsArea = document.querySelector("results");
            resultsArea.innerHTML = "";
            resultsArea.classList.remove("hidden");
            var stats = document.getElementById("stats");
            stats.innerHTML = "";
            stats.classList.remove("hidden");
            document.querySelector("pages").innerHTML = "";
            document.querySelector("pages").classList.remove("hidden");
            if (jason.d && jason.d.length > 0) {
                var rows = jason.d.split(row_delimiter);
                //console.log("[*] jason.d:", jason.d);
                //console.log("[*] rows:", rows);
                var totalHits = null;
                if (rows.length > 0) {
                    for (var row of rows) {
                        var values = row.split(value_delimiter);
                        if (values.length < 3 && totalHits === null) {
                            totalHits = values[0];
                            console.log("[*] totalHits", totalHits);
                            var hits = parseInt(totalHits, 10);
                            stats.appendChild(document.createTextNode("There are " + hits + " results for `" + searchParam + "`."));
                            var pages = document.querySelector("pages");
                            pages.onclick = getPage.bind(null, searchParam);
                            doPagination(hits, pageNum);
                            continue;
                        } else if (values.length === 3) {
                            resultsArea.appendChild(resultFactory(values));
                        }
                    }
                } else {
                    resultsArea.appendChild(document.createTextNode("No Articles matched your search :("));
                }
            } else {
                resultsArea.appendChild(document.createTextNode("No Articles matched your search :("));
            }
        } catch (e) {
            errorOut("search Error: ", e, resp);
        }
    }

    function getPage(searchParam, event) {
        if (event.target.tagName === "A") {
            var a = event.target;
            var pageNum = parseInt(a.textContent, 10) - 1;
            if (!isNaN(pageNum)) {
                document.querySelector("pages").classList.add("hidden");
                search(searchParam, pageNum).then(displaySearchResults.bind(null, searchParam, pageNum), errorOut);
            } else {
                // TODO do something with '...' case
            }
        }
    }

    function doPagination(hits, pageNum) {
        var pages = document.querySelector("pages");
        var range = 2; // two buttons on either side
        var pagination = 20; // this comes from the server and will need to be changed accordingly...
        var totalPages = hits / pagination | 0 + (hits % pagination) > 0;
        console.log("[*] totalPages", totalPages);
        var addButton = function pageButton(num) {
            console.log("[*] Num:", num);
            var a = document.createElement("a");
            a.href = "#";
            var text = null;
            if (typeof num === "number") {
                text = num + 1;
                a.href = "#" + num;
            } else {
                a.classList.add("expansion");
                text = num;
            }
            if (num === pageNum) {
                a.classList.add("active");
            }
            a.appendChild(document.createTextNode(text))
            return a;
        }

        if (pageNum - 3 >= 0) { // if our current page is not within 3 of first page, add ellipses
            console.log("[!] pagenum - 3 >= 0");
            pages.appendChild(addButton(0)); // add the first index, as it will always be there
            pages.appendChild(addButton("..."));
        }
        var pButton = null;
        var nButton = null;
        var deferred = [];
        var inferred = [];
        for (var i = 0; i <= range && i + range <= totalPages; i++) {
            var pos = pageNum + i;
            var neg = pageNum - i;
            if (pos === neg) {
                pButton = nButton = addButton(pageNum);
                pages.appendChild(pButton);
            } else {
                if (pos < totalPages) {
                    console.log("[*] ib pos:", pos);
                    pages.appendChild(addButton(pos));
                } else {
                    console.log("[*] oob pos:", pos, "=>", neg - range);
                    inferred.push(addButton(neg - range));
                }
                if (neg >= 0) {
                    console.log("[*] ib neg:", neg);
                    var nTmp = addButton(neg);
                    pages.insertBefore(nTmp, nButton);
                    nButton = nTmp;
                } else {
                    console.log("[*] oob neg:", neg, "=>", pos + range);
                    deferred.push(addButton(pos + range));
                }
            }
        }
        for (var defer of deferred) {
            pages.appendChild(defer);
        }
        for (var infer of inferred) {
            pages.insertBefore(infer, nButton);
            nButton = infer;
        }
        if (pageNum + range < totalPages - 1) {
            console.log("[!] pagenum + 3 <= totalPages");
            pages.appendChild(addButton("..."));
            pages.appendChild(addButton(totalPages - 1));
        }
    }

    function resultFactory(URLobject) {
        //console.log("[*] URLobject: ", URLobject);
        var url = URLobject[0];
        var title = URLobject[1];
        var ts = URLobject[2];
        var result = document.createElement("result");
        var anchor = document.createElement("a");
        var time = document.createElement("span");
        var earl = document.createElement("span");
        earl.classList.add("url");
        time.classList.add("timestamp");
        time.appendChild(document.createTextNode(ts));
        result.appendChild(time);
        anchor.href = url;
        anchor.appendChild(document.createTextNode(title));
        result.appendChild(anchor);
        result.appendChild(document.createElement("br"));
        result.appendChild(earl).appendChild(document.createTextNode(url));
        return result;
    }

    function outputReset() {
        var o = document.querySelector("output");
        o.innerHTML = "";
        o.classList.remove("active");
    }

    function displayResults(input, results) {
        try {
            outputReset();
            var o = document.querySelector("output");
            var jason = JSON.parse(results);
            if (jason.d) {
                for (var elem of jason.d) {
                    if (elem && elem.length > input.length) {
                        o.classList.add("active");
                        var a = document.createElement("a");
                        var s = document.createElement("strong");
                        a.href = "#";
                        a.setAttribute("data-value", elem);
                        a.onclick = setQuery;
                        // FIXME:
                        var same = "";
                        for (var i in input) {
                            try {
                                if (elem[i] === input[i]) {
                                    same += elem[i];
                                } else {
                                    break;
                                }
                            } catch (e) { }
                        }
                        //o.appendChild(a);

                        o.appendChild(a).appendChild(s).appendChild(document.createTextNode(same));
                        a.appendChild(document.createTextNode(elem.slice(same.length)));
                        //a.appendChild(document.createTextNode(elem));
                    }
                }
            }
        } catch (error) {
            errorOut("[-] displayResults Error!", jason, results);
        }
    }

    function setQuery(event) {
        var sb = document.getElementById("searchBox");
        sb.value = event.target.getAttribute("data-value");
        query({ "target": sb }).then(displayResults, errorOut);
    }

    function errorOut() {
        for (var x of arguments) {
            console.error(x);
        }
    }

    function getNBAData(searchString) {
        return new Promise(function (resolve, reject) {
            var xhr = new XMLHttpRequest();
            xhr.open("GET", "http://spencerwalden.net/info344a/projects/1/query.php?player_name=" + searchString);
            xhr.onload = function (meh) { resolve(xhr.responseText); };
            xhr.onerror = function (a, b, c) { reject(a, b, c); };
            xhr.send();
        });
    }

    function displayNBAStats(resp) {
        try {
            var loc = document.querySelector("nbastats");
            loc.classList.add("hidden");
            var table = loc.querySelector("table");
            table.innerHTML = ""; // clear
            var jason = JSON.parse(resp);
            if (jason[0]) { // check 0 since we're only getting one person, ever.
                loc.classList.remove("hidden");
                var headRow = table.appendChild(document.createElement("thead")).appendChild(document.createElement("tr"));
                var bodyRow = table.appendChild(document.createElement("tbody")).appendChild(document.createElement("tr"));
                for (var key in jason[0]) {
                    headRow.appendChild(document.createElement("th")).appendChild(document.createTextNode(key));
                    bodyRow.appendChild(document.createElement("td")).appendChild(document.createTextNode(jason[0][key]));
                }
            }
        } catch (e) {
            errorOut("[-] Error in display NBAStats:", resp);
        }
    }

    function query(event) {
        return new Promise(function (resolve, reject) {
            var data = {};
            data["word"] = event.target.value;
            var xhr = new XMLHttpRequest();
            xhr.onload = function (meh) { resolve(xhr.response); };
            xhr.onerror = function (a, b, c) { reject(a, b, c); };
            xhr.open("POST", "/Admin.asmx/Query");
            xhr.setRequestHeader("Content-Type", "application/json; charset=utf-8");
            xhr.send(JSON.stringify(data));
        });
    }


    function search(value, pageNum) {
        return new Promise(function (resolve, reject) {
            var data = {
                "search": value,
                "page": pageNum
            };
            var xhr = new XMLHttpRequest();
            xhr.onload = function (meh) { resolve(xhr.response); };
            xhr.onerror = function (a, b, c) { reject(a, b, c); };
            xhr.open("POST", "/Admin.asmx/Search");
            xhr.setRequestHeader("Content-Type", "application/json; charset=utf-8");
            xhr.send(JSON.stringify(data));
        });
    }
})();