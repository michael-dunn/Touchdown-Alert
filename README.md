# TouchdownAlert

Watches an ESPN fantasy football league and plays a sound whenever a starter on a
team you're rooting for scores a touchdown. Runs as a small ASP.NET Core app with
a live dashboard on your laptop or phone.

## Prerequisites

- .NET 10 SDK
- Windows (the audio player uses NAudio's Windows waveOut backend)
- A public (or accessible) ESPN fantasy football league

## Project layout

```
src/
  TouchdownAlert.Core/        Parser, touchdown detector, alert router, sound resolver, DI wiring
  TouchdownAlert.App/         This app: ASP.NET Core host, poller, audio playback, SignalR hub, dashboard
  TouchdownAlert.Simulator/   Fake ESPN server for testing without waiting on a real game
tests/
  TouchdownAlert.Core.Tests/
  TouchdownAlert.IntegrationTests/
sounds/                       Your alert sound files (git-ignored except sounds/README.md)
```

## Configuring watched teams and sounds

Edit `src/TouchdownAlert.App/appsettings.json` (or, better, create a git-ignored
`src/TouchdownAlert.App/appsettings.Local.json` with just the parts you want to
override — it's loaded after `appsettings.json` so you can keep your league id,
team ids, and sound choices out of the repo).

Drop `.mp3` or `.wav` files into the `sounds/` folder at the repo root and
reference them by file name:

```json
{
  "Leagues": [
    { "Key": "main", "Provider": "Espn", "LeagueId": "12345678" }
  ],
  "Sounds": {
    "Directory": "sounds",
    "Volume": 1.0,
    "MaxDurationSeconds": 5
  },
  "Alerts": {
    "WatchedTeams": [
      { "TeamId": 1, "League": "main", "Label": "Michael", "SoundFile": "airhorn.mp3" },
      { "TeamId": 4, "League": "main", "Label": "Alex", "SoundFile": "duck.mp3" },
      { "TeamId": 7, "League": "main", "Label": "Priya", "SoundFile": "horn.mp3" },
      { "TeamId": 10, "League": "main", "Label": "Sam", "SoundFile": "slide-whistle.mp3" }
    ]
  }
}
```

Clips are cut off after `MaxDurationSeconds` (default 5) so a long file can't drown out the
next alert; set it to `0` to always play files in full.

Up to four teams are supported. `TeamId` is the fantasy team id within its league
(for ESPN, visible in the ESPN app/site URL or team settings). When one touchdown
involves players started by more than one watched team, sounds play in the order
the teams are listed.

### Multiple leagues

`Leagues` is an array, so a watched team can live in a different league than the others —
useful if you're in more than one fantasy league, or once other providers are supported.
Each league needs a unique `Key` (your own short name, e.g. `"main"`); each watched team's
`League` references that key. If you only configure one league, `League` on a watched team
can be omitted and defaults to it; with more than one league configured, every watched team
must say which one it's in.

```json
{
  "Leagues": [
    { "Key": "main", "Provider": "Espn", "LeagueId": "12345678" },
    { "Key": "work", "Provider": "Yahoo", "LeagueId": "nfl.l.987654" }
  ],
  "Alerts": {
    "WatchedTeams": [
      { "TeamId": 1, "League": "main", "Label": "Michael", "SoundFile": "airhorn.mp3" },
      { "TeamId": 3, "League": "work", "Label": "Michael (work league)", "SoundFile": "airhorn.mp3" }
    ]
  }
}
```

Each `LeagueOptions` entry also accepts `BaseUrl` (provider default when omitted; point it at
the simulator for local testing), `SeasonId`, `ScoringPeriodId`, and `RequestTimeoutSeconds` —
the same per-league settings that used to live under the single `Espn` section.

### Yahoo leagues

Yahoo leagues need an OAuth2 login (Yahoo doesn't offer public read access the way ESPN does for
non-private leagues), so there's a one-time setup step:

1. Create a Yahoo developer app at https://developer.yahoo.com/apps/create/ - Fantasy Sports,
   **read** permission is enough. Set the redirect URI to `oob` (out of band - Yahoo shows you a
   code to copy instead of redirecting to a URL).
2. Put the app's Client ID/Secret in a git-ignored `src/TouchdownAlert.App/appsettings.Local.json`:

   ```json
   {
     "Yahoo": { "ClientId": "your-client-id", "ClientSecret": "your-client-secret" },
     "Leagues": [
       { "Key": "main", "Provider": "Espn", "LeagueId": "12345678" },
       { "Key": "yahoo", "Provider": "Yahoo", "LeagueId": "123456" }
     ]
   }
   ```

   `LeagueId` can be a bare numeric league id (assumed to be in the `nfl` game for the current
   season) or a full league key like `461.l.123456`.
3. Run the app (`dotnet run --project src/TouchdownAlert.App`) and open
   **http://localhost:5055/setup/yahoo**. Click "Open Yahoo login", log in, approve access, and
   Yahoo shows you a short code. Paste it into the page and submit.

That's it - the app polls the Yahoo league from then on. The saved token lives at
`config/yahoo-token.json` (configurable via `Yahoo:TokenFilePath`) and is refreshed automatically;
`config/` is git-ignored (except `config/README.md`) so the token never gets committed. If the
token is ever invalid/expired and can't be refreshed, that league's dashboard chip shows
"Yahoo: not logged in - open /setup/yahoo" instead of breaking anything else - just repeat step 3.
Missing `Yahoo:ClientId`/`ClientSecret` for a configured Yahoo league fails fast at startup with a
message telling you to add them to `appsettings.Local.json`; not being logged in yet does not.

Yahoo rosters are only fetched for teams you're actually watching (`Alerts:WatchedTeams`) - Yahoo
rate-limits aggressively, and the dashboard/alerts never need any other team's lineup.

If a configured `SoundFile` can't be found, the dashboard still shows the alert —
it just flags the sound as missing instead of silently failing.

To run without audio entirely (e.g. on a headless machine or in CI), either set
`"Sounds": { "Enabled": false }` in config or set the environment variable
`TOUCHDOWNALERT_SILENT=1` before starting the app. Both select a no-op player
that logs what it would have played instead of touching an audio device.

## Running

```
dotnet run --project src/TouchdownAlert.App
```

Then open the dashboard at **http://localhost:5055**.

The app polls ESPN every `Polling:IntervalSeconds` (default 30s, editable live —
no restart needed) and pushes updates to the dashboard over SignalR. From the
dashboard you can:

- Hit **Poll now** to force an immediate poll instead of waiting for the timer.
- Hit **Re-seed detector** to reset touchdown tracking (useful after changing
  `ScoringPeriodId`/week, or if state looks stuck).
- Hit **Test sound** on any watched team's card to fire a fake touchdown alert
  end-to-end (plays the sound, logs it, flashes the card) without waiting for a
  real score.

During the offseason/preseason, ESPN returns empty rosters — the dashboard shows
"No lineup yet" for each team rather than breaking.

## Running tests

```
dotnet test
```

## Simulating a live game

The simulator is a fake ESPN API with its own control console, so you can rehearse a whole
game day without an NFL game on.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\run-sim-demo.ps1
```

That opens two windows: the simulator console at http://localhost:5199 and the app dashboard at
http://localhost:5055, polling the simulator every 5 seconds. Click a TD button next to any
starter on a watched team in the simulator, or use **Random TD** / **Autoplay**, and the dashboard
flashes and your sound plays a few seconds later.

Manual equivalent:

```powershell
dotnet run --project src/TouchdownAlert.Simulator
dotnet run --project src/TouchdownAlert.App -- --Leagues:0:BaseUrl=http://localhost:5199 --Polling:IntervalSeconds=5
```

Full control API and details: `src/TouchdownAlert.Simulator/README.md`.

## HTTP API

- `GET /api/state` — current dashboard view model as JSON
- `GET /api/health` — `{ ok, lastPollAt, lastError }`
- `POST /api/poll` — trigger an immediate poll
- `POST /api/detector/reset` — reset the touchdown detector for every league (re-seeds on the next poll)
- `POST /api/detector/reset/{leagueKey}` — reset the touchdown detector for one league
- `POST /api/test/{leagueKey}/{teamId}` — fire a test alert for a watched team in that league (400 if not watched)
- `POST /api/test/{teamId}` — legacy form; works when the team id is unambiguous across all watched teams (400 if not watched, or if watched in more than one league)

SignalR hub is at `/hub`; it pushes a `state` event after every poll and an
`alert` event whenever an alert fires.
