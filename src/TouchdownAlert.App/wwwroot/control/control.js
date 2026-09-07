(function () {
  "use strict";

  // ---- state -------------------------------------------------------------

  /** Latest settings document from the server (GET /api/settings or the "settings" hub push). */
  let serverSettings = null;
  /** Latest dashboard state (GET /api/state or the "state" hub push) - used for the lineups section. */
  let dashboardState = null;
  /** Working copy of leagues/watchedTeams/polling/sounds being edited (sections 2-4). Overlay saves immediately
   * and is not part of this draft. */
  let draft = null;
  let dirty = false;

  const els = {};
  document.querySelectorAll("[id]").forEach((el) => { els[toCamel(el.id)] = el; });

  function toCamel(id) {
    return id.replace(/-([a-z0-9])/g, (_, c) => c.toUpperCase());
  }

  // ---- fetch helpers -------------------------------------------------------

  async function getJson(url) {
    const res = await fetch(url);
    if (!res.ok) throw new Error(`${url} -> ${res.status}`);
    return res.json();
  }

  async function putJson(url, body) {
    const res = await fetch(url, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    const data = await res.json().catch(() => ({}));
    return { ok: res.ok, status: res.status, data };
  }

  // ---- draft management -----------------------------------------------------

  function makeDraft(settings) {
    return {
      leagues: settings.leagues.map((l) => ({ ...l })),
      watchedTeams: settings.watchedTeams.map((t) => ({ ...t })),
      polling: { ...settings.polling },
      sounds: { ...settings.sounds },
    };
  }

  function markDirty() {
    dirty = true;
    els.saveBar && els.saveBar.classList.add("dirty");
  }

  function applyServerSettings(settings, { resetDraft } = { resetDraft: false }) {
    serverSettings = settings;
    if (!draft || resetDraft) {
      draft = makeDraft(settings);
      dirty = false;
    }
    renderAll();
  }

  // ---- rendering -------------------------------------------------------------

  function renderAll() {
    if (!serverSettings || !draft) return;
    renderRestartBanner();
    renderLeagueChips();
    renderLeaguesTable();
    renderTeamsList();
    renderPollingSounds();
    renderOverlay();
    renderLineups();
    renderSaveBar();
  }

  function renderRestartBanner() {
    els.restartBanner.hidden = !serverSettings.meta.restartRequired;
  }

  function renderLeagueChips() {
    const chips = (dashboardState?.leagues ?? []).map((l) => {
      const cls = l.lastError ? "bad" : (l.lastPollAt ? "good" : "");
      const week = l.week != null ? `wk ${l.week}` : "";
      const lastPoll = l.lastPollAt ? new Date(l.lastPollAt).toLocaleTimeString() : "never polled";
      return `<div class="league-chip ${cls}">
        <span class="league-chip-name">${escapeHtml(l.key)} (${escapeHtml(l.provider)})</span>
        <span class="league-chip-meta">${escapeHtml(l.name || "")} ${escapeHtml(week)} - ${escapeHtml(lastPoll)}</span>
        ${l.lastError ? `<span class="league-chip-error">${escapeHtml(l.lastError)}</span>` : ""}
      </div>`;
    });
    els.leagueChips.innerHTML = chips.join("") || `<span class="empty">No leagues configured yet.</span>`;
  }

  function renderLeaguesTable() {
    const rows = draft.leagues.map((league, i) => `
      <tr data-index="${i}">
        <td><input type="text" class="league-key" value="${escapeAttr(league.key)}" /></td>
        <td>
          <select class="league-provider">
            <option value="Espn" ${league.provider === "Espn" ? "selected" : ""}>Espn</option>
            <option value="Yahoo" ${league.provider === "Yahoo" ? "selected" : ""}>Yahoo</option>
            <option value="Sleeper" ${league.provider === "Sleeper" ? "selected" : ""}>Sleeper</option>
          </select>
        </td>
        <td><input type="text" class="league-id" value="${escapeAttr(league.leagueId)}" /></td>
        <td><button type="button" class="remove-league">Remove</button></td>
      </tr>`);
    els.leaguesBody.innerHTML = rows.join("") || `<tr><td colspan="4" class="empty">No leagues yet - add one below.</td></tr>`;

    els.leaguesBody.querySelectorAll("tr").forEach((tr) => {
      const i = Number(tr.dataset.index);
      tr.querySelector(".league-key").addEventListener("input", (e) => { draft.leagues[i].key = e.target.value; markDirty(); renderSaveBar(); });
      tr.querySelector(".league-provider").addEventListener("change", (e) => { draft.leagues[i].provider = e.target.value; markDirty(); renderTeamsList(); renderSaveBar(); });
      tr.querySelector(".league-id").addEventListener("input", (e) => { draft.leagues[i].leagueId = e.target.value; markDirty(); renderSaveBar(); });
      tr.querySelector(".remove-league").addEventListener("click", () => {
        const key = draft.leagues[i].key;
        draft.leagues.splice(i, 1);
        draft.watchedTeams = draft.watchedTeams.filter((t) => t.league !== key);
        markDirty();
        renderAll();
      });
    });

    const yahooLeagues = draft.leagues.filter((l) => l.provider === "Yahoo");
    els.yahooHint.hidden = yahooLeagues.length === 0;
    if (yahooLeagues.length > 0) {
      const y = serverSettings.meta.yahoo;
      els.yahooStatus.textContent = y.isConfigured
        ? (y.isLoggedIn ? "logged in" : "configured, not logged in")
        : "Yahoo:ClientId/ClientSecret not configured in appsettings.Local.json";
    }

    els.sleeperHint.hidden = !draft.leagues.some((l) => l.provider === "Sleeper");
  }

  function renderTeamsList() {
    const container = els.teamsList;
    const leagueOptions = draft.leagues.map((l) => `<option value="${escapeAttr(l.key)}">${escapeHtml(l.key)}</option>`).join("");

    container.innerHTML = draft.watchedTeams.map((team, i) => {
      const leagueTeams = serverSettings.meta.leagueTeams[team.league] || [];
      const known = leagueTeams.length > 0;
      const teamOptions = known
        ? leagueTeams.map((t) => `<option value="${t.teamId}" ${t.teamId === team.teamId ? "selected" : ""}>${escapeHtml(t.name)}</option>`).join("")
        : "";

      const teamPicker = known
        ? `<select class="team-teamid">${teamOptions}</select>`
        : `<input type="number" class="team-teamid" value="${team.teamId}" title="Poll first to see team names" />`;

      const soundOptions = serverSettings.meta.availableSounds
        .map((s) => `<option value="${escapeAttr(s)}" ${s === team.soundFile ? "selected" : ""}>${escapeHtml(s)}</option>`)
        .join("");

      return `<div class="team-row" data-index="${i}">
        <div class="order-btns">
          <button type="button" class="team-up" ${i === 0 ? "disabled" : ""}>▲</button>
          <button type="button" class="team-down" ${i === draft.watchedTeams.length - 1 ? "disabled" : ""}>▼</button>
        </div>
        <select class="team-league">${leagueOptions}</select>
        ${teamPicker}
        <input type="text" class="team-label" value="${escapeAttr(team.label || "")}" placeholder="Label" />
        <div class="team-color-group">
          <input type="color" class="team-color-picker" value="${escapeAttr(team.color || "#22c55e")}" />
          <input type="text" class="team-color-text" value="${escapeAttr(team.color || "")}" placeholder="#rrggbb" />
        </div>
        <select class="team-sound"><option value="">(none)</option>${soundOptions}</select>
        <div>
          <button type="button" class="team-test" ${dirty ? "disabled title='Save first to test the current setup'" : ""}>▶ Test</button>
          <button type="button" class="team-remove">Remove</button>
        </div>
      </div>`;
    }).join("") || `<div class="empty">No watched teams yet.</div>`;

    // Pre-select league on the <select> since template above sets `selected` only on options we generated inline for teamOptions, not leagueOptions.
    container.querySelectorAll(".team-row").forEach((row) => {
      const i = Number(row.dataset.index);
      row.querySelector(".team-league").value = draft.watchedTeams[i].league || "";

      row.querySelector(".team-league").addEventListener("change", (e) => {
        draft.watchedTeams[i].league = e.target.value;
        markDirty();
        renderTeamsList();
      });
      row.querySelector(".team-teamid").addEventListener("change", (e) => {
        draft.watchedTeams[i].teamId = Number(e.target.value);
        const known = (serverSettings.meta.leagueTeams[draft.watchedTeams[i].league] || []).find((t) => t.teamId === Number(e.target.value));
        if (known && !draft.watchedTeams[i].label) {
          draft.watchedTeams[i].label = known.name;
        }
        markDirty();
        renderTeamsList();
      });
      row.querySelector(".team-label").addEventListener("input", (e) => { draft.watchedTeams[i].label = e.target.value; markDirty(); renderSaveBar(); });
      row.querySelector(".team-color-picker").addEventListener("input", (e) => {
        draft.watchedTeams[i].color = e.target.value;
        row.querySelector(".team-color-text").value = e.target.value;
        markDirty();
        renderSaveBar();
      });
      row.querySelector(".team-color-text").addEventListener("input", (e) => {
        draft.watchedTeams[i].color = e.target.value;
        if (/^#[0-9a-fA-F]{6}$/.test(e.target.value)) {
          row.querySelector(".team-color-picker").value = e.target.value;
        }
        markDirty();
        renderSaveBar();
      });
      row.querySelector(".team-sound").addEventListener("change", (e) => { draft.watchedTeams[i].soundFile = e.target.value; markDirty(); renderSaveBar(); });
      row.querySelector(".team-remove").addEventListener("click", () => {
        draft.watchedTeams.splice(i, 1);
        markDirty();
        renderTeamsList();
        renderSaveBar();
      });
      row.querySelector(".team-up").addEventListener("click", () => {
        if (i === 0) return;
        [draft.watchedTeams[i - 1], draft.watchedTeams[i]] = [draft.watchedTeams[i], draft.watchedTeams[i - 1]];
        markDirty();
        renderTeamsList();
      });
      row.querySelector(".team-down").addEventListener("click", () => {
        if (i === draft.watchedTeams.length - 1) return;
        [draft.watchedTeams[i + 1], draft.watchedTeams[i]] = [draft.watchedTeams[i], draft.watchedTeams[i + 1]];
        markDirty();
        renderTeamsList();
      });

      const testBtn = row.querySelector(".team-test");
      if (!dirty) {
        testBtn.addEventListener("click", async () => {
          const team = draft.watchedTeams[i];
          try {
            const res = await fetch(`/api/test/${encodeURIComponent(team.league)}/${team.teamId}`, { method: "POST" });
            if (!res.ok) {
              const body = await res.json().catch(() => ({}));
              alert("Test failed: " + (body.error || res.statusText));
            }
          } catch (err) {
            alert(String(err));
          }
        });
      }
    });

    els.btnAddTeam.disabled = draft.watchedTeams.length >= 4;
  }

  function renderPollingSounds() {
    els.pollingInterval.value = draft.polling.intervalSeconds;
    els.soundsVolume.value = draft.sounds.volume;
    els.soundsVolumeValue.textContent = Math.round(draft.sounds.volume * 100) + "%";
    els.soundsMaxDuration.value = draft.sounds.maxDurationSeconds;
  }

  function renderOverlay() {
    const overlay = serverSettings.overlay;
    els.overlayEnabled.checked = overlay.enabled;
    els.overlayLocked.checked = overlay.locked;
    els.overlayScale.value = Math.round(overlay.scale * 100);
    els.overlayScaleValue.textContent = Math.round(overlay.scale * 100) + "%";
    els.overlayOpacity.value = Math.round(overlay.opacity * 100);
    els.overlayOpacityValue.textContent = Math.round(overlay.opacity * 100) + "%";
    els.overlayDisplay.value = overlay.display;
    els.overlayPosition.textContent = (overlay.x == null || overlay.y == null)
      ? "default"
      : `${Math.round(overlay.x)}, ${Math.round(overlay.y)}`;
  }

  function renderLineups() {
    const teams = dashboardState?.watchedTeams ?? [];
    if (teams.length === 0) {
      els.lineups.innerHTML = `<span class="empty">No poll data yet.</span>`;
      return;
    }

    els.lineups.innerHTML = teams.map((team) => {
      const starterRows = team.starters.map((p) => `
        <tr>
          <td>${escapeHtml(p.slot)}</td>
          <td>${escapeHtml(p.name)}</td>
          <td>${escapeHtml(p.position)}</td>
          <td>${p.points.toFixed(1)}</td>
          <td>${p.touchdownTotal > 0 ? `<span class="td-badge">${p.touchdownTotal}</span>` : ""}</td>
        </tr>`).join("");

      const benchRows = team.bench.map((p) => `
        <tr><td>${escapeHtml(p.slot)}</td><td>${escapeHtml(p.name)}</td><td>${escapeHtml(p.position)}</td><td>${p.points.toFixed(1)}</td><td>${p.touchdownTotal > 0 ? `<span class="td-badge">${p.touchdownTotal}</span>` : ""}</td></tr>`).join("");

      return `<div class="lineup-card" style="border-left:3px solid ${escapeAttr(team.color)}">
        <h3>${escapeHtml(team.label)}</h3>
        ${team.hasRoster ? `
          <table class="starters">
            <thead><tr><th>Slot</th><th>Player</th><th>Pos</th><th>Pts</th><th>TD</th></tr></thead>
            <tbody>${starterRows}</tbody>
          </table>
          <details class="bench"><summary>Bench (${team.bench.length})</summary>
            <table class="starters"><tbody>${benchRows}</tbody></table>
          </details>` : `<div class="empty">No roster yet.</div>`}
      </div>`;
    }).join("");
  }

  function renderSaveBar() {
    els.btnSave.disabled = !dirty;
    els.btnDiscard.disabled = !dirty;
  }

  // ---- actions ---------------------------------------------------------------

  async function save() {
    const body = {
      leagues: draft.leagues.map((l) => ({
        key: l.key, provider: l.provider, leagueId: l.leagueId,
        baseUrl: l.baseUrl ?? null, seasonId: l.seasonId ?? null, scoringPeriodId: l.scoringPeriodId ?? null,
      })),
      watchedTeams: draft.watchedTeams.map((t) => ({
        teamId: t.teamId, league: t.league, label: t.label || null, soundFile: t.soundFile || "", color: t.color || null,
      })),
      polling: { intervalSeconds: Number(draft.polling.intervalSeconds) },
      sounds: { volume: Number(draft.sounds.volume), maxDurationSeconds: Number(draft.sounds.maxDurationSeconds) },
      overlay: serverSettings.overlay,
    };

    const { ok, data } = await putJson("/api/settings", body);
    if (!ok) {
      showSaveErrors(data.errors || ["Save failed."]);
      return;
    }

    hideSaveErrors();
    showToast("Settings saved.");
    dirty = false;
    const settings = await getJson("/api/settings");
    applyServerSettings(settings, { resetDraft: true });
  }

  function discard() {
    draft = makeDraft(serverSettings);
    dirty = false;
    hideSaveErrors();
    renderAll();
  }

  function showSaveErrors(errors) {
    els.saveErrors.hidden = false;
    els.saveErrors.innerHTML = errors.map((e) => `<div>${escapeHtml(e)}</div>`).join("");
  }
  function hideSaveErrors() {
    els.saveErrors.hidden = true;
    els.saveErrors.innerHTML = "";
  }
  function showToast(text) {
    els.saveToast.hidden = false;
    els.saveToast.textContent = text;
    setTimeout(() => { els.saveToast.hidden = true; }, 3000);
  }

  async function saveOverlayField(patch) {
    const overlay = { ...serverSettings.overlay, ...patch };
    const { ok, data } = await putJson("/api/settings/overlay", overlay);
    if (!ok) {
      alert("Overlay save failed: " + (data.errors || []).join(", "));
      return;
    }
    serverSettings = { ...serverSettings, overlay: data.overlay };
    renderOverlay();
  }

  function addLeague() {
    draft.leagues.push({ key: "", provider: "Espn", leagueId: "", baseUrl: null, seasonId: null, scoringPeriodId: null });
    markDirty();
    renderAll();
  }

  function addTeam() {
    if (draft.watchedTeams.length >= 4) return;
    const firstLeague = draft.leagues[0]?.key || "";
    draft.watchedTeams.push({ teamId: 0, league: firstLeague, label: "", soundFile: "", color: null });
    markDirty();
    renderTeamsList();
    renderSaveBar();
  }

  // ---- wiring ------------------------------------------------------------------

  function wireStaticControls() {
    els.btnAddLeague.addEventListener("click", addLeague);
    els.btnAddTeam.addEventListener("click", addTeam);
    els.btnSave.addEventListener("click", save);
    els.btnDiscard.addEventListener("click", discard);

    els.btnPoll.addEventListener("click", async () => { await fetch("/api/poll", { method: "POST" }); });
    els.btnReseed.addEventListener("click", async () => { await fetch("/api/detector/reset", { method: "POST" }); });

    const restart = async () => {
      if (!confirm("Restart TouchdownAlert now? Any unsaved edits here will be lost.")) return;
      await fetch("/api/restart", { method: "POST" });
      showToast("Restarting...");
    };
    els.btnRestart.addEventListener("click", restart);
    els.btnRestart2.addEventListener("click", restart);

    els.pollingInterval.addEventListener("input", (e) => { draft.polling.intervalSeconds = Number(e.target.value); markDirty(); renderSaveBar(); });
    els.soundsVolume.addEventListener("input", (e) => {
      draft.sounds.volume = Number(e.target.value);
      els.soundsVolumeValue.textContent = Math.round(draft.sounds.volume * 100) + "%";
      markDirty();
      renderSaveBar();
    });
    els.soundsMaxDuration.addEventListener("input", (e) => { draft.sounds.maxDurationSeconds = Number(e.target.value); markDirty(); renderSaveBar(); });

    els.overlayEnabled.addEventListener("change", (e) => saveOverlayField({ enabled: e.target.checked }));
    els.overlayLocked.addEventListener("change", (e) => saveOverlayField({ locked: e.target.checked }));
    els.overlayScale.addEventListener("change", (e) => saveOverlayField({ scale: Number(e.target.value) / 100 }));
    els.overlayOpacity.addEventListener("change", (e) => saveOverlayField({ opacity: Number(e.target.value) / 100 }));
    els.overlayDisplay.addEventListener("change", (e) => saveOverlayField({ display: e.target.value }));
    els.btnOverlayReset.addEventListener("click", () => saveOverlayField({ x: null, y: null }));

    els.overlayScale.addEventListener("input", (e) => { els.overlayScaleValue.textContent = e.target.value + "%"; });
    els.overlayOpacity.addEventListener("input", (e) => { els.overlayOpacityValue.textContent = e.target.value + "%"; });

    window.addEventListener("beforeunload", (e) => {
      if (dirty) { e.preventDefault(); e.returnValue = ""; }
    });
  }

  // ---- hub ---------------------------------------------------------------------

  function connectHub() {
    if (window.__signalrFailed || typeof signalR === "undefined") {
      els.liveText.textContent = "live updates unavailable";
      return;
    }

    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/hub")
      .withAutomaticReconnect()
      .build();

    connection.on("state", (state) => { dashboardState = state; renderLineups(); renderLeagueChips(); });
    connection.on("settings", (settings) => { applyServerSettings(settings); });

    connection.onreconnecting(() => setConn(false));
    connection.onreconnected(() => setConn(true));
    connection.onclose(() => setConn(false));

    connection.start().then(() => setConn(true)).catch(() => setConn(false));
  }

  function setConn(on) {
    els.liveDot.className = "dot " + (on ? "dot-on" : "dot-off");
    els.liveText.textContent = on ? "live" : "disconnected";
  }

  // ---- utils ---------------------------------------------------------------

  function escapeHtml(s) {
    return String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  }
  function escapeAttr(s) { return escapeHtml(s); }

  // ---- boot ------------------------------------------------------------------

  async function init() {
    wireStaticControls();
    connectHub();
    try {
      const [settings, state] = await Promise.all([getJson("/api/settings"), getJson("/api/state")]);
      dashboardState = state;
      applyServerSettings(settings, { resetDraft: true });
    } catch (err) {
      document.querySelector("main").innerHTML = `<div class="panel"><p class="empty">Could not load settings: ${escapeHtml(String(err))}</p></div>`;
    }
  }

  init();
})();
