# Sleeper provider plan

League: **1401782105192570880** ("Blood, Sweat and Beers", 12 teams, 2026 season, public read API, no auth).
Roster positions: QB, RB, RB, WR, WR, TE, FLEX, FLEX, K, DEF, 5 BN, 1 IR (reserve slot).

## Verified API facts (probed 2026-09-06, host `https://api.sleeper.app`)

| Endpoint | Gives | Notes |
| --- | --- | --- |
| `GET /v1/state/nfl` | `week`, `display_week`, `season`, `season_type` | current NFL week (auto week detection) |
| `GET /v1/league/{id}` | `name`, `season`, `settings.num_teams`, `roster_positions`, `scoring_settings` | |
| `GET /v1/league/{id}/users` | `user_id`, `display_name`, `metadata.team_name` (optional) | team name = `metadata.team_name` else display_name |
| `GET /v1/league/{id}/rosters` | `roster_id` (int, 1..12), `owner_id`, `players[]`, `starters[]`, `reserve[]` | `roster_id` is the WatchedTeam `TeamId` |
| `GET /v1/league/{id}/matchups/{week}` | per roster: `matchup_id`, `points`, `starters[]`, `players[]`, `players_points{}` | pairs share `matchup_id`; null = bye. Only fantasy points, **no stat lines** |
| `GET /v1/stats/nfl/regular/{season}/{week}` | `{ player_id: { stat: value } }` for every player that week | ~570 KB (gzip it). Empty `{}` before games. This is where TD counts come from |
| `GET /v1/players/nfl` | full player dictionary, ~14.6 MB, 12k entries | Sleeper asks for at most one call per day; cache a trimmed copy on disk |

Player ids are numeric strings (`"4881"`); team defenses use the abbreviation (`"DET"`).
Starters array order follows `roster_positions`; a `"0"` entry is an empty slot.

## TD stat keys (union over all 18 weeks of 2025)

Count these, per player:

| Sleeper key | TouchdownType |
| --- | --- |
| `pass_td` | Passing |
| `rush_td` | Rushing |
| `rec_td` | Receiving |
| `kr_td` | KickReturn |
| `pr_td` | PuntReturn |
| `fum_rec_td` | FumbleReturn |
| `blk_kick_ret_td`, `blk_pr_td` | BlockedKickReturn |
| `def_td`, `idp_def_td` | Defensive (players: IDP only) |
| `misc_td` | Defensive (misc; verify it is not already in `st_td`) |

For **DEF units** the per-week stats endpoint only exposes `td` (an aggregate: points-allowed logic uses it, it counts *opponent* TDs allowed, e.g. `BAL td:5` with -2 pts). DEF scoring TDs appear as `def_td` / `st_td` / `def_st_td` only in the league-scoring sense; in 2025 data no DEF row carried `def_td`/`st_td`, so **DEF touchdowns must be detected from `def_st_td`/`def_td`/`st_td` if present, never from `td`**. Treat `td` and `anytime_tds` as aggregates and never count them.

For individual players, `st_td` is an aggregate of `kr_td` + `pr_td` + `misc_td`/blocked-kick returns: do not count it alongside those (double count). Ignore `pass_int_td` (QB threw a pick-six), `first_td`, `*_lng`, `*_40p`, `*_50p`, `bonus_*`.

## Design (mirrors the Yahoo provider)

Core (`src/TouchdownAlert.Core/Sleeper/`):
- `LeagueProvider.Sleeper` enum value; `LeagueOptions.BaseUrl` default `https://api.sleeper.app`.
- `SleeperWire.cs`: System.Text.Json DTOs for state, league, users, rosters, matchups, stats, trimmed player.
- `SleeperStatMap.cs`: the table above, `TouchdownCounts FromSleeperStats(IReadOnlyDictionary<string,double>)`.
- `SleeperPlayerDirectory`: id -> (full name, position, NFL team). Trimmed cache at `config/sleeper-players.json` (git-ignored via `config/*`), refreshed when older than 24 h, stale cache used if the download fails. Unknown id -> `"Player {id}"`. DEF ids map to a fixed negative `long` per NFL abbreviation (RosteredPlayer.PlayerId is `long`).
- `SleeperLeagueSource : ILeagueSource`: per poll fetch state (once per poll, cheap), league (cache name/season), users+rosters (cache, refresh every N polls or when a starter id is missing), matchups/{week}, stats/{season}/{week}. Build a `TeamSnapshot` for all 12 rosters: starters get `LineupSlotId` from position order (QB=0,RB=2,WR=4,TE=6,FLEX=23,K=17,DEF=16), bench = 20, reserve = 21. Player points from `players_points`, team points from `points`. `MatchupSnapshot` pairs by `matchup_id`, lower roster_id as home.
- `SleeperApiException`, DI wiring in `ServiceCollectionExtensions` (named HttpClient with `AutomaticDecompression`), validator accepts Sleeper with no credentials.

App:
- `control.js` provider dropdown gains `Sleeper`; no login hint needed.
- README section for Sleeper (league id from the app URL, team id = roster id shown in the control page picker).

Simulator: `SleeperEmulation.cs` serving `/v1/state/nfl`, `/v1/league/{id}`, `/users`, `/rosters`, `/matchups/{week}`, `/v1/stats/nfl/regular/{season}/{week}`, `/v1/players/nfl` over the same `SimulatedLeague`, translating ESPN stat ids to Sleeper keys.

Tests: `SleeperStatMapTests`, `SleeperSnapshotMapperTests` (fixture JSON captured from the real league), `SleeperPlayerDirectoryTests` (cache TTL, stale fallback), `SimulatorSleeperEndpointTests`, `SleeperEndToEndTests` (ESPN + Sleeper from one simulator, each watched team alerts once).

## Agent split
1. Core wire DTOs + stat map + player directory (+ unit tests).
2. `SleeperLeagueSource` + DI + validator + enum (+ unit tests with fixtures), depends on 1.
3. Simulator emulation + simulator endpoint tests, parallel with 2.
4. Control page + README + settings example; e2e test after 2 and 3.
5. Final: full `dotnet test`, add the real league to `config/settings.json` with `Key: "sleeper"`, pick the watched roster id.
