(function () {
  "use strict";

  const els = {
    leagueName: document.getElementById("league-name"),
    connDot: document.getElementById("conn-dot"),
    connText: document.getElementById("conn-text"),
    weekLabel: document.getElementById("week-label"),
    pollInfo: document.getElementById("poll-info"),
    leagueChips: document.getElementById("league-chips"),
    errorBanner: document.getElementById("error-banner"),
    teams: document.getElementById("teams"),
    alertLog: document.getElementById("alert-log"),
    btnPoll: document.getElementById("btn-poll"),
    btnReset: document.getElementById("btn-reset"),
  };

  let nextPollAt = null;
  let countdownTimer = null;

  function fmtTime(iso) {
    if (!iso) return "--";
    const d = new Date(iso);
    return d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
  }

  function fmtPoints(p) {
    return p === null || p === undefined ? "--" : Number(p).toFixed(2);
  }

  function renderPollInfo(state) {
    const poll = state.poll || {};
    nextPollAt = poll.nextPollAt ? new Date(poll.nextPollAt) : null;
    updateCountdown();

    if (poll.lastError) {
      els.errorBanner.hidden = false;
      els.errorBanner.textContent = "Last poll failed: " + poll.lastError;
    } else {
      els.errorBanner.hidden = true;
      els.errorBanner.textContent = "";
    }
  }

  function updateCountdown() {
    const poll = window.__lastState && window.__lastState.poll;
    const lastPollAt = poll ? poll.lastPollAt : null;
    let countdownText = "";
    if (nextPollAt) {
      const secs = Math.max(0, Math.round((nextPollAt - new Date()) / 1000));
      countdownText = "next poll in " + secs + "s";
    }
    els.pollInfo.textContent = "last poll " + fmtTime(lastPollAt) + " • " + countdownText;
  }

  function renderHeader(state) {
    els.leagueName.textContent = state.leagueName ? "– " + state.leagueName : "";
    els.weekLabel.textContent = state.week ? "Week " + state.week : "Preseason / waiting";
    renderPollInfo(state);
    renderLeagueChips(state);
  }

  function leagueChipHtml(league) {
    const statusClass = league.lastError ? "bad" : (league.detectorSeeded ? "good" : "pending");
    const weekText = league.week ? "Week " + league.week : "waiting";
    const name = league.name || league.key;
    return (
      '<div class="league-chip ' + statusClass + '" data-league-key="' + esc(league.key) + '">' +
      '<span class="league-chip-name">' + esc(name) + "</span>" +
      '<span class="league-chip-meta">' + esc(league.provider) + " • " + esc(weekText) + "</span>" +
      (league.lastError ? '<span class="league-chip-error">' + esc(league.lastError) + "</span>" : "") +
      "</div>"
    );
  }

  function renderLeagueChips(state) {
    const leagues = state.leagues || [];
    if (leagues.length <= 1) {
      els.leagueChips.innerHTML = "";
      els.leagueChips.hidden = true;
      return;
    }
    els.leagueChips.hidden = false;
    els.leagueChips.innerHTML = leagues.map(leagueChipHtml).join("");
  }

  function playerRow(p) {
    const tds = p.touchdownTotal > 0 ? '<span class="td-badge">' + p.touchdownTotal + " TD</span>" : "";
    return "<tr><td>" + esc(p.slot) + "</td><td>" + esc(p.name) + "</td><td>" + esc(p.position) +
      "</td><td>" + fmtPoints(p.points) + "</td><td>" + tds + "</td></tr>";
  }

  function esc(s) {
    if (s === null || s === undefined) return "";
    return String(s).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  }

  function teamCardHtml(team) {
    const soundClass = team.soundFound ? "found" : "missing";
    const soundText = team.soundFound
      ? team.soundFile
      : team.soundFile + " (missing, expected at " + (team.soundResolvedPath || team.soundFile) + ")";

    let body;
    if (!team.hasRoster) {
      body = '<div class="no-lineup">No lineup yet</div>';
    } else {
      const starterRows = team.starters.map(playerRow).join("");
      const benchRows = team.bench.map(playerRow).join("");
      body =
        '<table class="starters"><thead><tr><th>Slot</th><th>Player</th><th>Pos</th><th>Pts</th><th>TD</th></tr></thead>' +
        "<tbody>" + (starterRows || '<tr><td colspan="5" class="empty">No starters</td></tr>') + "</tbody></table>" +
        (team.bench.length
          ? '<details class="bench"><summary>Bench (' + team.bench.length + ")</summary>" +
            '<table class="starters"><tbody>' + benchRows + "</tbody></table></details>"
          : "");
    }

    const opponent = team.opponent
      ? '<div class="score-vs">vs ' + esc(team.opponent.name) + '<br><span class="score-opp">' + fmtPoints(team.opponent.points) + "</span></div>"
      : '<div class="score-vs">no matchup</div>';

    return (
      '<article class="team-card" data-team-id="' + team.teamId + '" data-league-key="' + esc(team.leagueKey) + '">' +
      '<div class="team-card-header"><span class="team-label">' + esc(team.label) + "</span>" +
      '<span class="team-espn-name">' + esc(team.espnTeamName || "") + "</span></div>" +
      '<div class="league-badge">' + esc(team.leagueName || team.leagueKey) + "</div>" +
      '<div class="score-row"><span class="score-mine">' + fmtPoints(team.points) + "</span>" + opponent + "</div>" +
      '<div class="sound-status ' + soundClass + '">' + esc(soundText) + "</div>" +
      '<button class="test-btn" data-team-id="' + team.teamId + '" data-league-key="' + esc(team.leagueKey) + '">Test sound</button>' +
      body +
      "</article>"
    );
  }

  function renderTeams(state) {
    const teams = state.watchedTeams || [];
    if (teams.length === 0) {
      els.teams.innerHTML = '<p class="empty">No watched teams configured. Add entries under Alerts:WatchedTeams in appsettings.json.</p>';
      return;
    }
    els.teams.innerHTML = teams.map(teamCardHtml).join("");
  }

  function alertRowHtml(a, showLeague) {
    const testBadge = a.isTest ? '<span class="test-badge">TEST</span>' : "";
    const soundNote = a.soundFound ? "" : " (sound missing)";
    const leagueBadge = showLeague ? '<span class="alert-league">' + esc(a.leagueKey) + "</span>" : "";
    return (
      "<li><span class=\"alert-time\">" + fmtTime(a.at) + "</span>" +
      leagueBadge +
      '<span class="alert-team">' + esc(a.teamLabel) + "</span>" +
      "<span>" + esc(a.playerName) + " — " + esc(a.touchdownType) +
      (a.count > 1 ? " x" + a.count : "") + soundNote + "</span>" +
      testBadge + "</li>"
    );
  }

  function renderAlerts(state) {
    const alerts = state.recentAlerts || [];
    const showLeague = (state.leagues || []).length > 1;
    els.alertLog.innerHTML = alerts.length
      ? alerts.map((a) => alertRowHtml(a, showLeague)).join("")
      : '<li class="empty">No alerts yet</li>';
  }

  function flashTeam(teamId, leagueKey) {
    const selector = leagueKey
      ? '[data-team-id="' + teamId + '"][data-league-key="' + leagueKey + '"]'
      : '[data-team-id="' + teamId + '"]';
    const card = els.teams.querySelector(selector);
    if (!card) return;
    card.classList.remove("flash");
    // force reflow so the animation restarts if it's already flashing
    void card.offsetWidth;
    card.classList.add("flash");
  }

  function renderState(state) {
    window.__lastState = state;
    renderHeader(state);
    renderTeams(state);
    renderAlerts(state);
  }

  els.teams.addEventListener("click", async (e) => {
    const btn = e.target.closest(".test-btn");
    if (!btn) return;
    const teamId = btn.dataset.teamId;
    const leagueKey = btn.dataset.leagueKey;
    btn.disabled = true;
    try {
      const url = leagueKey ? "/api/test/" + leagueKey + "/" + teamId : "/api/test/" + teamId;
      const res = await fetch(url, { method: "POST" });
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        alert("Test alert failed: " + (body.error || res.statusText));
      }
    } catch (err) {
      alert("Test alert failed: " + err);
    } finally {
      btn.disabled = false;
    }
  });

  els.btnPoll.addEventListener("click", () => fetch("/api/poll", { method: "POST" }));
  els.btnReset.addEventListener("click", () => fetch("/api/detector/reset", { method: "POST" }));

  setInterval(updateCountdown, 1000);

  async function loadInitialState() {
    try {
      const res = await fetch("/api/state");
      if (res.ok) {
        renderState(await res.json());
      }
    } catch (err) {
      // hub connection will retry; ignore
    }
  }

  function setConnectionStatus(connected, text) {
    els.connDot.className = "dot " + (connected ? "dot-on" : "dot-off");
    els.connText.textContent = text;
  }

  loadInitialState();

  if (window.__signalrFailed || typeof signalR === "undefined") {
    setConnectionStatus(false, "live updates unavailable (SignalR failed to load)");
    return;
  }

  const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hub")
    .withAutomaticReconnect()
    .build();

  connection.on("state", renderState);
  connection.on("alert", (alert) => {
    flashTeam(alert.teamId, alert.leagueKey);
  });

  connection.onreconnecting(() => setConnectionStatus(false, "reconnecting..."));
  connection.onreconnected(() => {
    setConnectionStatus(true, "connected");
    loadInitialState();
  });
  connection.onclose(() => setConnectionStatus(false, "disconnected"));

  connection
    .start()
    .then(() => setConnectionStatus(true, "connected"))
    .catch(() => setConnectionStatus(false, "connection failed"));
})();
