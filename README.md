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
  "Espn": {
    "LeagueId": 12345678
  },
  "Sounds": {
    "Directory": "sounds",
    "Volume": 1.0
  },
  "Alerts": {
    "WatchedTeams": [
      { "TeamId": 1, "Label": "Michael", "SoundFile": "airhorn.mp3" },
      { "TeamId": 4, "Label": "Alex", "SoundFile": "duck.mp3" },
      { "TeamId": 7, "Label": "Priya", "SoundFile": "horn.mp3" },
      { "TeamId": 10, "Label": "Sam", "SoundFile": "slide-whistle.mp3" }
    ]
  }
}
```

Up to four teams are supported. `TeamId` is the ESPN fantasy team id within the
league (visible in the ESPN app/site URL or team settings). When one touchdown
involves players started by more than one watched team, sounds play in the order
the teams are listed.

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

For testing touchdown detection without waiting on a real NFL game, point
`Espn:BaseUrl` at the simulator instead of ESPN's live API. See
`src/TouchdownAlert.Simulator/README.md` for how to run and drive it.

## HTTP API

- `GET /api/state` — current dashboard view model as JSON
- `GET /api/health` — `{ ok, lastPollAt, lastError }`
- `POST /api/poll` — trigger an immediate poll
- `POST /api/detector/reset` — reset the touchdown detector (re-seeds on the next poll)
- `POST /api/test/{teamId}` — fire a test alert for a watched team (400 if the team isn't watched)

SignalR hub is at `/hub`; it pushes a `state` event after every poll and an
`alert` event whenever an alert fires.
