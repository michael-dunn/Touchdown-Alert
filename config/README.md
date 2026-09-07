# config/

Holds runtime files that shouldn't be checked in:

- **`settings.json`** - the only place `Leagues`, `Alerts:WatchedTeams`, `Polling:IntervalSeconds`,
  `Sounds:Volume`/`MaxDurationSeconds`, and `Overlay` live. Edit it from the control page
  (http://localhost:5055/control) or by hand - it has the same shape as `settings.example.json`
  (the committed template). If it's missing, the app creates one on startup from that template
  with empty `Leagues`/`Alerts:WatchedTeams`, and logs where to go (the control page) to fill them
  in. Most edits apply live; adding/removing a league or changing its provider/id needs a restart
  (the control page will tell you).
- `yahoo-token.json` - the saved Yahoo OAuth access/refresh token pair, written after you log in at
  http://localhost:5055/setup/yahoo (see the "Yahoo leagues" section of the top-level README).
- `sleeper-players.json` - a trimmed cache of Sleeper's players dictionary (`GET /v1/players/nfl`:
  id -> name, position, NFL team), written on the first poll of a Sleeper league and refreshed once
  a day (Sleeper asks for at most one download per day). Path configurable via
  `Sleeper:PlayersCacheFilePath` (see the "Sleeper leagues" section of the top-level README).

Everything in this folder except this file and `settings.example.json` is git-ignored. Deleting
`yahoo-token.json` (or using the "Log out" button on the setup page) forces a fresh Yahoo login
next time the app polls a Yahoo league. Deleting `sleeper-players.json` forces a fresh download of
the players dictionary on the next Sleeper poll. Deleting `settings.json` resets you to zero
leagues/teams on next startup (it gets recreated automatically).
