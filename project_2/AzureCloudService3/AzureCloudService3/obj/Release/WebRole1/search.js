(function () {
    "use strict";
    var prev = "";
    window.onload = function () {
        var sb = document.getElementById("searchBox");
        var tb = document.getElementById("tmpbtn");
        var o = document.querySelector("output");
        tb.onclick = function (event) {
            query({ "target": sb }).then(displayResults.bind(null, sb.value.trim()), errorOut);

        }
        sb.onkeyup = function (event) {
            if (/^[a-z\d ]+$/i.test(event.which)) {
                if (sb.value.trim()) {
                    query(event).then(displayResults.bind(null, sb.value.trim()), errorOut);
                }
            }
            if (sb.value === "") {
                outputReset();
            }
        }
        document.body.onclick = outputReset;
    };

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
        query({"target": sb}).then(displayResults, errorOut);
    }

    function errorOut() {
        for (var x of arguments) {
            console.error(x);
        }
    }

    function query(event) {
        return new Promise(function (resolve, reject) {
            var data = {
                "word": event.target.value
            }
            var xhr = new XMLHttpRequest();
            xhr.onload = function (meh) { resolve(xhr.response) };
            xhr.onerror = function (a, b, c) { reject(a, b, c) };
            xhr.open("POST", "/WikiSearch.asmx/Query");
            xhr.setRequestHeader("Content-Type", "application/json; charset=utf-8");
            xhr.send(JSON.stringify(data));
        });
    }
})();