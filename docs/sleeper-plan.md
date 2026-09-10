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

Implemented in `SleeperStatMap.FromStats(stats, isTeamDefense)`. Rows whose id starts with `TEAM_` are NFL team aggregates and are dropped before mapping (`SleeperStatsParser`).

**Individual player rows** (numeric id):

| Sleeper key | TouchdownType | Rule |
| --- | --- | --- |
| `pass_td` | Passing | |
| `rush_td` | Rushing | |
| `rec_td` | Receiving | |
| `st_td` | Return | Aggregate of kick/punt/blocked-kick return TDs. Preferred whenever present. |
| `kr_td` | KickReturn | Only when `st_td` is **absent** |
| `pr_td` | PuntReturn | Only when `st_td` is **absent** |
| `blk_kick_ret_td`, `blk_pr_td` | BlockedKickReturn | Only when `st_td` is **absent** |
| `fum_rec_td` | FumbleReturn | `fum_rec_ez_tds` duplicates it and is ignored |
| `idp_def_td`, `def_td` | Defensive | `def_td` only if Sleeper ever puts it on a player row |

Never add `misc_td` on top of `st_td` - it duplicates the blocked-kick component.

**Team-defense rows** (id is an NFL abbreviation, position `DEF`):

| Sleeper key | TouchdownType | Rule |
| --- | --- | --- |
| `def_td` | Defensive | INT/fumble return TD |
| `def_st_td` | Return | Kick/punt/blocked-kick return TD |

Do **not** count `misc_td` on DEF rows (duplicate of the blocked-kick portion of `def_st_td`), and do **not** count `td` - on a DEF row that is *opponent* touchdowns allowed (e.g. `"BAL": {"td":5,"pts_std":-2}`).

**Never counted anywhere**: `td`, `anytime_tds`, `first_td`, `pass_int_td` (the QB threw a pick-six), `misc_td`, `fum_rec_ez_tds`, and every `*_lng`, `*_40p`, `*_50p`, `bonus_*` variant.

## Design (mirrors the Yahoo provider)

Core (`src/TouchdownAlert.Core/Sleeper/`, implemented):
- `LeagueProvider.Sleeper` enum value; `SleeperOptions` (section `Sleeper`: `ApiBaseUrl` default `https://api.sleeper.app`, `PlayersCacheFilePath` default `config/sleeper-players.json`, `PlayersCacheMaxAgeHours` default 24). A league's `BaseUrl` overrides `ApiBaseUrl`.
- `SleeperWire.cs`: System.Text.Json DTOs for state, league, users, rosters, matchups, player; `SleeperStatsParser` for the stats dictionary (drops `TEAM_*` rows, tolerates non-numeric values).
- `SleeperStatMap.FromStats(stats, isTeamDefense)`: the tables above.
- `SleeperIds`: numeric id -> `long`; NFL abbreviation -> fixed negative id (-1..-32, alphabetical table); anything else -> deterministic FNV hash below -1000. `IsTeamDefense` = 2-3 upper-case letters.
- `SleeperLineupSlots`: roster_positions entry -> ESPN slot id (QB=0, RB=2, WR=4, TE=6, FLEX=23, K=17, DEF=16 shown as "D/ST", BN=20 shown as "BE", IR=21; unknown starting slot -> 23 with the raw name). Position `DEF` is displayed as `D/ST` like ESPN.
- `SleeperPlayerDirectory`: id -> (full name, position, NFL team). Trimmed cache (git-ignored via `config/*`) written atomically (temp + move), re-downloaded when older than the TTL, stale cache used if the download fails (with a 10-minute retry back-off), in-memory after the first read. Unknown id -> `"Player {id}"` / DEF -> the abbreviation.
- `SleeperSnapshotMapper.Map(...)`: pure. Every roster is a team (bye weeks included); starters first in roster_positions order (matchup row's `starters`/`players` preferred over the roster's, `"0"` skipped without shifting later slots), then bench, then `reserve` as IR. Team name = `metadata.team_name`, else `"Team {display_name}"`, else `"Team {roster_id}"`. Player points from `players_points`, team points from `points`, `MatchupSnapshot` pairs by `matchup_id` with the lower roster_id as home.
- `SleeperLeagueSource : ILeagueSource`: per poll `v1/state/nfl` (skipped when both `SeasonId` and `ScoringPeriodId` are forced; week = `ScoringPeriodId ?? state.week`, season = `SeasonId ?? state.season`), then concurrently `v1/league/{id}` (cached after first success), `/users`, `/rosters`, `/matchups/{week}`, `v1/stats/nfl/regular/{season}/{week}`, plus the directory. Transport/JSON/status errors -> `SleeperApiException` with the URL.
- `SleeperDecompressionHandler`: a DelegatingHandler asking for gzip/br and decoding it. Deliberately NOT `SocketsHttpHandler.AutomaticDecompression`: `ConfigureHttpClientDefaults` runs *before* per-client configuration, so a per-client primary handler would override the integration tests' TestServer routing.
- DI in `ServiceCollectionExtensions`: shared `ISleeperPlayerDirectory` singleton on named client `sleeper-players` (60 s timeout), one `league:{key}` client per Sleeper league, `case LeagueProvider.Sleeper` in the source factory. Validator: Sleeper needs no credentials, `LeagueId` must be all digits.

App:
- `control.js` provider dropdown gains `Sleeper`; no login hint needed.
- README section for Sleeper (league id from the app URL, team id = roster id shown in the control page picker).

Simulator: `SleeperEmulation.cs` serving `/v1/state/nfl`, `/v1/league/{id}`, `/users`, `/rosters`, `/matchups/{week}`, `/v1/stats/nfl/regular/{season}/{week}`, `/v1/players/nfl` over the same `SimulatedLeague`, translating ESPN stat ids to Sleeper keys.

Tests (Core, done): `SleeperStatMapTests`, `SleeperIdsTests`, `SleeperSnapshotMapperTests` (fixture JSON captured from the real league), `SleeperPlayerDirectoryTests` (cache TTL, stale fallback, concurrency), `SleeperLeagueSourceTests` (routes, caching, errors), `SleeperDecompressionHandlerTests`, `SleeperDependencyInjectionTests` (full DI with ConfigureHttpClientDefaults routing). Elsewhere: `SimulatorSleeperEndpointTests`, `SleeperEndToEndTests` (ESPN + Sleeper from one simulator, each watched team alerts once).

## Agent split
1. Core wire DTOs + stat map + player directory (+ unit tests).
2. `SleeperLeagueSource` + DI + validator + enum (+ unit tests with fixtures), depends on 1.
3. Simulator emulation + simulator endpoint tests, parallel with 2.
4. Control page + README + settings example; e2e test after 2 and 3.
5. Final: full `dotnet test`, add the real league to `config/settings.json` with `Key: "sleeper"`, pick the watched roster id.

## Status (2026-09-06)

Implemented and tested on `feature/sleeper`:
- Core: everything under "Design" above (`SleeperLeagueSource`, `SleeperPlayerDirectory` with the disk cache, `SleeperStatMap`, `SleeperIds`, `SleeperLineupSlots`, `SleeperSnapshotMapper`, `SleeperDecompressionHandler`, DI wiring, `LeagueProvider.Sleeper`, `SleeperOptions`, validator rule) plus the Core unit tests listed under "Tests".
- Simulator: `SleeperEmulation` + `/v1/*` routes over the shared simulated league; `SimulatorSleeperEndpointTests`.
- App: `Sleeper` in the control page's provider dropdown with a one-line hint; settings API/validation accept Sleeper leagues with no credentials; dashboard/control page show the provider per league.
- End to end: `SleeperEndToEndTests` - ESPN + Sleeper leagues from one simulator each alert exactly once (receiving TD via `rec_td`), a D/ST interception-return TD on the abbreviation-keyed `def_td` row raises a Defensive alert, and the Sleeper dashboard shows all 10 named rosters with fully named starters (player directory + mapper path).
- Docs: README "Sleeper leagues" section, `config/README.md` entry for `sleeper-players.json`.

Remaining (by hand, on the user's machine):
1. Add the real league to `config/settings.json` (control page or by hand): `{ "Key": "sleeper", "Provider": "Sleeper", "LeagueId": "1401782105192570880" }`, restart, then pick the watched roster id from the control page's team picker after the first poll.
2. First live verification on game day: confirm the first poll downloads `config/sleeper-players.json`, that the watched team's starters are named, and that a real touchdown raises exactly one alert in the Sleeper league.
