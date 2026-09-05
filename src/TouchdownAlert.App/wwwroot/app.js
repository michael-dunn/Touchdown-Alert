(function () {
  "use strict";

  const els = {
    leagueChips: document.getElementById("league-chips"),
    liveDot: document.getElementById("live-dot"),
    liveUpdated: document.getElementById("live-updated"),
    liveError: document.getElementById("live-error"),
    tiles: document.getElementById("tiles"),
    auditBody: document.getElementById("audit-body"),
    banner: document.getElementById("td-banner"),
    bannerText: document.getElementById("td-banner-text"),
    bannerTest: document.getElementById("td-banner-test"),
  };

  const reduceMotion =
    window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  let lastState = null;
  let connected = false;

  // teamKey -> { displayed, target, raf, prevPoints }
  const tileAnim = new Map();

  function teamKey(leagueKey, teamId) {
    return (leagueKey || "") + "::" + teamId;
  }

  function esc(s) {
    if (s === null || s === undefined) return "";
    return String(s).replace(/[&<>"']/g, (c) => ({
      "&": "&amp;",
      "<": "&lt;",
      ">": "&gt;",
      '"': "&quot;",
      "'": "&#39;",
    }[c]));
  }

  function fmtPoints(p) {
    return p === null || p === undefined ? "\u2014" : Number(p).toFixed(1);
  }

  function fmtClockTime(iso) {
    if (!iso) return "\u2014";
    const d = new Date(iso);
    return d.toLocaleTimeString([], { hour: "numeric", minute: "2-digit", second: "2-digit" });
  }

  function fmtAgo(iso) {
    if (!iso) return null;
    const secs = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 1000));
    if (secs < 60) return secs + "s ago";
    const mins = Math.round(secs / 60);
    return mins + "m ago";
  }

  // ---------- Header ----------

  function leagueChipHtml(league) {
    const name = league.name || league.key;
    const weekText = league.week ? "Week " + league.week : "waiting";
    return (
      '<div class="league-chip">' +
      '<span class="league-chip-name">' + esc(name) + "</span>" +
      "<span>\u00b7 " + esc(league.provider) + " \u00b7 " + esc(weekText) + "</span>" +
      "</div>"
    );
  }

  function renderLeagueChips(state) {
    const leagues = state.leagues || [];
    els.leagueChips.innerHTML = leagues.map(leagueChipHtml).join("");
  }

  function renderLiveStatus(state) {
    if (!connected) {
      els.liveDot.className = "live-dot live-dot-off";
      els.liveUpdated.textContent = "reconnecting\u2026";
      els.liveError.textContent = "";
      return;
    }

    const leagues = state.leagues || [];
    const poll = state.poll || {};
    const intervalSecs = poll.intervalSeconds || 30;
    const staleThresholdMs = intervalSecs * 2 * 1000;

    const errored = leagues.find((l) => l.lastError);
    let newestPollAt = null;
    for (const l of leagues) {
      if (l.lastPollAt) {
        const t = new Date(l.lastPollAt).getTime();
        if (!newestPollAt || t > newestPollAt) newestPollAt = t;
      }
    }
    const isStale = !newestPollAt || (Date.now() - newestPollAt) > staleThresholdMs;

    if (errored) {
      els.liveDot.className = "live-dot live-dot-bad";
      els.liveError.textContent = errored.lastError;
    } else if (isStale) {
      els.liveDot.className = "live-dot live-dot-stale";
      els.liveError.textContent = "";
    } else {
      els.liveDot.className = "live-dot live-dot-good";
      els.liveError.textContent = "";
    }

    els.liveUpdated.textContent = newestPollAt
      ? "updated " + fmtAgo(new Date(newestPollAt).toISOString())
      : "no polls yet";
  }

  // ---------- Tiles ----------

  function leagueForTeam(state, team) {
    return (state.leagues || []).find((l) => l.key === team.leagueKey);
  }

  function tileStaleInfo(state, team) {
    const league = leagueForTeam(state, team);
    const poll = state.poll || {};
    const intervalSecs = poll.intervalSeconds || 30;
    const staleThresholdMs = intervalSecs * 2 * 1000;
    const lastPollAt = league ? league.lastPollAt : null;
    if (!lastPollAt) return { stale: true, ago: null };
    const ageMs = Date.now() - new Date(lastPollAt).getTime();
    return { stale: ageMs > staleThresholdMs, ago: fmtAgo(lastPollAt) };
  }

  function tileHtml(team, state) {
    const key = teamKey(team.leagueKey, team.teamId);
    const stale = tileStaleInfo(state, team);
    const color = team.color || "#5b6270";

    let scoreInner;
    let subLine = "";
    if (team.points === null || team.points === undefined) {
      scoreInner = "\u2014";
      if (!team.hasRoster) subLine = '<div class="tile-sub">No lineup yet</div>';
    } else {
      scoreInner =
        '<span class="tile-score-num">' + fmtPoints(team.points) + "</span>" +
        '<span class="tile-td-count">(' + (team.touchdownTotal || 0) + ")</span>";
    }

    const staleNote = stale.stale && stale.ago
      ? '<div class="tile-stale-note">updated ' + esc(stale.ago) + "</div>"
      : "";

    return (
      '<div class="tile' + (stale.stale ? " stale" : "") + '" data-team-key="' + esc(key) +
      '" style="--team-color:' + esc(color) + '; --tile-tint:' + tintColor(color) + '">' +
      '<div class="tile-label">' + esc(team.label) + "</div>" +
      '<div class="tile-league">' + esc(team.leagueName || team.leagueKey) + "</div>" +
      '<div class="tile-score" id="score-' + esc(key) + '">' + scoreInner + "</div>" +
      subLine +
      staleNote +
      "</div>"
    );
  }

  function tintColor(hex) {
    const rgb = hexToRgb(hex);
    if (!rgb) return "#0d1117";
    return "rgba(" + rgb.r + "," + rgb.g + "," + rgb.b + ",0.12)";
  }

  function hexToRgb(hex) {
    if (!hex) return null;
    const m = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex.trim());
    if (!m) return null;
    return { r: parseInt(m[1], 16), g: parseInt(m[2], 16), b: parseInt(m[3], 16) };
  }

  function renderTiles(state) {
    const teams = state.watchedTeams || [];
    els.tiles.className = "tiles-grid count-" + Math.min(teams.length, 4);

    if (teams.length === 0) {
      els.tiles.innerHTML = '<div class="tiles-empty">No teams configured &mdash; open /control</div>';
      return;
    }

    els.tiles.innerHTML = teams.map((t) => tileHtml(t, state)).join("");

    // Handle count-up + flash for each team whose points increased.
    for (const team of teams) {
      const key = teamKey(team.leagueKey, team.teamId);
      const newPoints = typeof team.points === "number" ? team.points : null;
      let anim = tileAnim.get(key);
      if (!anim) {
        anim = { prevPoints: newPoints, displayed: newPoints };
        tileAnim.set(key, anim);
        continue;
      }
      const prev = anim.prevPoints;
      anim.prevPoints = newPoints;
      if (newPoints !== null && prev !== null && newPoints > prev) {
        animateScore(key, prev, newPoints);
      }
    }
  }

  function animateScore(key, from, to) {
    const el = document.getElementById("score-" + key);
    if (!el) return;
    const numEl = el.querySelector(".tile-score-num");
    if (!numEl) return;

    el.classList.add("flash");
    setTimeout(() => el.classList.remove("flash"), 1500);

    if (reduceMotion) {
      return;
    }

    const duration = 700;
    const start = performance.now();

    function step(now) {
      const elapsed = now - start;
      const t = Math.min(1, elapsed / duration);
      const eased = 1 - Math.pow(1 - t, 3);
      const val = from + (to - from) * eased;
      numEl.textContent = fmtPoints(val);
      if (t < 1) {
        requestAnimationFrame(step);
      } else {
        numEl.textContent = fmtPoints(to);
      }
    }
    requestAnimationFrame(step);
  }

  function pulseTile(leagueKey, teamId) {
    const key = teamKey(leagueKey, teamId);
    const tile = els.tiles.querySelector('[data-team-key="' + cssEscape(key) + '"]');
    if (!tile) return;
    tile.classList.remove("pulse");
    void tile.offsetWidth;
    tile.classList.add("pulse");
  }

  function cssEscape(s) {
    return String(s).replace(/["\\]/g, "\\$&");
  }

  // ---------- TD banner queue ----------

  const bannerQueue = [];
  let bannerShowing = false;

  function teamColorFor(leagueKey, teamId) {
    if (!lastState) return "#3fd67a";
    const team = (lastState.watchedTeams || []).find(
      (t) => t.leagueKey === leagueKey && String(t.teamId) === String(teamId)
    );
    return (team && team.color) || "#3fd67a";
  }

  function enqueueAlertBanner(alert) {
    bannerQueue.push(alert);
    processBannerQueue();
  }

  function processBannerQueue() {
    if (bannerShowing || bannerQueue.length === 0) return;
    bannerShowing = true;
    const a = bannerQueue.shift();

    const color = teamColorFor(a.leagueKey, a.teamId);
    const type = (a.touchdownType || "").toLowerCase();
    let text = "TOUCHDOWN \u00b7 " + (a.teamLabel || "") + " \u00b7 " + (a.playerName || "");
    if (type) text += " \u00b7 " + type;
    if (a.count > 1) text += " \u00d7" + a.count;

    els.banner.style.setProperty("--banner-color", color);
    els.bannerText.textContent = text;
    els.bannerTest.hidden = !a.isTest;

    pulseTile(a.leagueKey, a.teamId);

    // force reflow so re-triggering the slide works even for back-to-back alerts
    els.banner.hidden = false;
    void els.banner.offsetWidth;
    els.banner.classList.add("show");

    setTimeout(() => {
      els.banner.classList.remove("show");
      setTimeout(() => {
        els.banner.hidden = true;
        bannerShowing = false;
        processBannerQueue();
      }, reduceMotion ? 0 : 350);
    }, 5000);
  }

  // ---------- Audit table ----------

  function auditRowHtml(a) {
    const testPill = a.isTest ? '<span class="audit-test-pill">TEST</span>' : "";
    const color = teamColorFor(a.leagueKey, a.teamId);
    return (
      "<tr>" +
      '<td class="col-time">' + esc(fmtClockTime(a.at)) + "</td>" +
      '<td class="col-team"><span class="audit-team-dot" style="--dot-color:' + esc(color) + '"></span>' +
      esc(a.teamLabel) + "</td>" +
      '<td class="col-player">' + esc(a.playerName) +
      '<span class="audit-td-type">' + esc(a.touchdownType) + "</span>" +
      testPill + "</td>" +
      "</tr>"
    );
  }

  function renderAudit(state) {
    const alerts = state.recentAlerts || [];
    if (alerts.length === 0) {
      els.auditBody.innerHTML = '<tr class="audit-empty"><td colspan="3">No touchdowns yet this session</td></tr>';
      return;
    }

    const cutoff = Date.now() - 5 * 60 * 1000;
    let visible = alerts.filter((a) => new Date(a.at).getTime() >= cutoff);
    if (visible.length < Math.min(5, alerts.length)) {
      visible = alerts.slice(0, 5);
    }

    els.auditBody.innerHTML = visible.map(auditRowHtml).join("");
  }

  // ---------- Master render ----------

  function renderAll(state) {
    lastState = state;
    renderLeagueChips(state);
    renderLiveStatus(state);
    renderTiles(state);
    renderAudit(state);
  }

  setInterval(() => {
    if (lastState) renderLiveStatus(lastState);
  }, 1000);

  setInterval(() => {
    if (lastState) renderAudit(lastState);
  }, 10000);

  async function loadInitialState() {
    try {
      const res = await fetch("/api/state");
      if (res.ok) {
        renderAll(await res.json());
      }
    } catch (err) {
      // hub connection will retry; ignore
    }
  }

  loadInitialState();

  if (window.__signalrFailed || typeof signalR === "undefined") {
    els.liveUpdated.textContent = "live updates unavailable (SignalR failed to load)";
    els.liveDot.className = "live-dot live-dot-bad";
  } else {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/hub")
      .withAutomaticReconnect()
      .build();

    connection.on("state", renderAll);
    connection.on("alert", (alert) => {
      enqueueAlertBanner(alert);
    });

    connection.onreconnecting(() => {
      connected = false;
      if (lastState) renderLiveStatus(lastState);
    });
    connection.onreconnected(() => {
      connected = true;
      loadInitialState();
    });
    connection.onclose(() => {
      connected = false;
      if (lastState) renderLiveStatus(lastState);
    });

    connection
      .start()
      .then(() => {
        connected = true;
        if (lastState) renderLiveStatus(lastState);
      })
      .catch(() => {
        connected = false;
        if (lastState) renderLiveStatus(lastState);
      });
  }
})();
