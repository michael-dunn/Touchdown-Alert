# TouchdownAlert.Simulator

A fake ESPN fantasy API server that emulates the one real endpoint TouchdownAlert polls, plus a control
API/page you can click during manual testing to drive a live game.

## Running it

```
dotnet run --project src/TouchdownAlert.Simulator
```

By default it listens on `http://localhost:5199` (override with `--urls` or `Urls` config / `ASPNETCORE_URLS`).

Open the control console at **http://localhost:5199/** - it shows all 10 teams, their starters and bench,
per-player "TD" buttons (one per touchdown type that player's position can score), a "Random TD" button per
team, week reset, and autoplay start/stop. It polls `/sim/state` every 2 seconds, so anything you click (or
that autoplay does) shows up within 2 seconds.

## Pointing the App at it

Run the App with the `main` league's `BaseUrl` set to the simulator and a short poll interval so alerts
show up fast:

```
dotnet run --project src/TouchdownAlert.App -- --Leagues:0:BaseUrl=http://localhost:5199 --Polling:IntervalSeconds=5
```

or add an `appsettings.Local.json` next to the App's `appsettings.json`:

```json
{
  "Leagues": [ { "Key": "main", "Provider": "Espn", "LeagueId": "998946988", "BaseUrl": "http://localhost:5199" } ],
  "Polling": { "IntervalSeconds": 5 }
}
```

The simulator's league id (998946988) and team ids (1-10, names from `fixtures/league-2026-preseason.json`)
match the App's default `Leagues:0:LeagueId` and any `Alerts:WatchedTeams` config that references those team
ids, so no other App-side config changes are needed.

## Demo walkthrough

1. Start the simulator: `dotnet run --project src/TouchdownAlert.Simulator`
2. Start the App pointed at it (see above).
3. Open the simulator console (`http://localhost:5199`) and the App dashboard (`http://localhost:5055`) side
   by side.
4. On the simulator console, find a starter on a team you're watching (team 1 by default) and click one of
   their TD buttons (e.g. "Rush" or "Rec"), or just click that team's "Random TD" button.
5. Within one App poll interval (5s in the command above) the App should detect the touchdown, play the
   configured sound, and show the alert on its dashboard / push it over SignalR.
6. Try "Start autoplay" (e.g. every 15s, focus teams `1,3`) to let the game run itself while you watch both
   pages react.

Or drive it with `Invoke-RestMethod` / curl instead of clicking:

```powershell
# Score a receiving TD for Ja'Marr Chase (id 4362628)
Invoke-RestMethod -Method Post http://localhost:5199/sim/touchdown -ContentType application/json `
    -Body '{"playerId":4362628,"type":"Receiving","count":1}'

# Random TD for team 1's random starter
Invoke-RestMethod -Method Post "http://localhost:5199/sim/touchdown/random?teamId=1"

# Start autoplay: a random event every 15s, weighted toward teams 1 and 3
Invoke-RestMethod -Method Post "http://localhost:5199/sim/autoplay/start?intervalSeconds=15&focusTeamIds=1,3"

# Reset to week 1 (fresh stats/points, same rosters)
Invoke-RestMethod -Method Post "http://localhost:5199/sim/reset?week=1"

# See the whole current league state
Invoke-RestMethod http://localhost:5199/sim/state | ConvertTo-Json -Depth 6

# The actual ESPN-shaped response the App's parser reads
Invoke-RestMethod "http://localhost:5199/apis/v3/games/ffl/seasons/2026/segments/0/leagues/998946988?view=mBoxscore"
```

curl equivalents:

```bash
curl -X POST http://localhost:5199/sim/touchdown -H "Content-Type: application/json" \
     -d '{"playerId":4362628,"type":"Receiving","count":1}'
curl -X POST "http://localhost:5199/sim/touchdown/random?teamId=1"
curl -X POST "http://localhost:5199/sim/autoplay/start?intervalSeconds=15&focusTeamIds=1,3"
curl -X POST "http://localhost:5199/sim/reset?week=1"
curl http://localhost:5199/sim/state
curl "http://localhost:5199/apis/v3/games/ffl/seasons/2026/segments/0/leagues/998946988?view=mBoxscore"
```

## Control API reference

| Method | Path | Notes |
|---|---|---|
| GET  | `/sim/state` | Compact summary: week, teams (starters/bench + points + stats), autoplay status, event log |
| POST | `/sim/reset?week=1` | Rebuilds the league deterministically for that week; all points/stats zeroed |
| POST | `/sim/touchdown` | Body `{ "playerId": 4362628, "type": "Receiving", "count": 1 }` |
| POST | `/sim/touchdown/random?teamId=1` | Random starter on that team scores an appropriate TD type for their position (no-op JSON response for K) |
| POST | `/sim/points` | Body `{ "playerId": 4362628, "points": 2.4 }` - arbitrary point bump, no stat change |
| POST | `/sim/autoplay/start?intervalSeconds=20&focusTeamIds=1,3` | Starts the background "live game"; `focusTeamIds` is optional and comma-separated |
| POST | `/sim/autoplay/stop` | Stops it |
| GET  | `/sim/players` | Every player id/name/position/pro-team plus which team(s)/slot(s) they're rostered in |

`type` accepts any `TouchdownType` name: `Passing`, `Rushing`, `Receiving`, `KickReturn`, `PuntReturn`,
`FumbleReturn`, `InterceptionReturn`, `BlockedKickReturn`.

## Notes on the simulated data

- Teams/ids/names come from `fixtures/league-2026-preseason.json` (10 teams, ids 1-10). Week-1 matchup
  pairings (3v9, 8v4, 6v5, 1v10, 2v7) also come from that fixture's real schedule.
- Rosters are deterministic (same players/ids every run): a handful of confirmed real ids from the project
  brief (Josh Allen 3918298 QB on team 1, Saquon Barkley 3929630 RB on team 2, Ja'Marr Chase 4362628 WR on
  team 3, Baltimore D/ST -16033 and Miami D/ST -16015), everyone else is an invented-but-unique id built
  from a pool of plausible real player names.
- **Deliberate test scenario:** Ja'Marr Chase (4362628) is a **starting** WR on team 3 and is *also* rostered
  on team 5's **bench**. In reality a player is only ever on one fantasy team - this is intentionally wrong
  so integration tests (and manual testing) can verify that a touchdown alert fires for team 3 (starter) but
  not for team 5 (benched), even though it's the same player/touchdown.
- The wire JSON is produced from `EspnLeagueResponse` and serialized with `EspnLeagueResponse.JsonOptions`,
  the same DTOs/options the real App's parser deserializes with.
