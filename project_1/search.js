(function() {
  "use strict";

  window.onload = function() {
    var s = document.getElementById("search");
    s.addEventListener("keyup", function(e) {
      player_search(e).then(display_results, error_out);
    });
  };

  function player_search(e) {
    return new Promise(function(resolve, reject) {
      //console.log(e.target.value);
      if (e.target.value.length > 2) {
        var xhr = new XMLHttpRequest();
        xhr.open("GET", "query.php?player_name=" + e.target.value);
        xhr.onload = function() { resolve(xhr.responseText); }
        xhr.send();
      }
    });
  }

  function error_out() {
    for (var e of arguments) {
      console.error(e);
    }
  }

  function display_results(response) {
    try {
      var jason = JSON.parse(response);
      document.querySelector("players").innerHTML = "";
      for (var i = 0; i < jason.length; i++) {
        render_player(jason[i]);
      }
    } catch(e) {
      error_out(e);
    }
  }

  function render_player(player) {
    var player_card = document.createElement("player");
    for (var key in player) {
      //console.log(key + ": " + player[key]);
      if (key === "Name") {
        var name = document.createElement("h1");
        name.appendChild(document.createTextNode(player[key]));
        player_card.appendChild(name);
        player_card.appendChild(document.createElement("hr"));
        var section = player_card.appendChild(document.createElement("section"));
        var table = section.appendChild(document.createElement("table"));
        table.classList.add("table-bordered", "table-hover", "table-striped");
        var tr = table.appendChild(document.createElement("tr"));
      } else if (key === "Team") {
        var img = document.createElement("img");
        img.setAttribute("src", "http://i.cdn.turner.com/nba/nba/assets/logos/teams/primary/web/"+player[key]+".svg");
        img.classList.add("team");
        section.appendChild(img);
      } else {
        tr.appendChild(document.createElement("th")).appendChild(document.createTextNode(key));
        tr.appendChild(document.createElement("td")).appendChild(document.createTextNode(player[key]));
        table.appendChild(tr);
        tr = document.createElement("tr");
      }
    }
    document.querySelector("players").appendChild(player_card);
    //console.log("[+] Player rendered.");
  }

})();
