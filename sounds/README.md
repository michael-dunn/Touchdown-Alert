# Sound files

Drop your alert sounds here. `mp3` and `wav` both work (wav starts a hair faster; mp3 is fine).

Reference them in `src/TouchdownAlert.App/appsettings.json` under `Alerts:WatchedTeams`, e.g.

```json
{ "TeamId": 1, "Label": "Michael", "SoundFile": "airhorn.mp3" }
```

Files in this folder are git-ignored (except this README) so you can keep personal clips out of the repo.
