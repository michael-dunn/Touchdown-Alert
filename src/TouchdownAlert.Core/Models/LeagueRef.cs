namespace TouchdownAlert.Core.Models;

public enum LeagueProvider { Espn, Yahoo, Sleeper }

/// <summary>Identifies one fantasy league across providers. Key is the user-chosen short name from config (e.g. "main").</summary>
public sealed record LeagueRef(string Key, LeagueProvider Provider, string LeagueId)
{
    public override string ToString() => $"{Key} ({Provider} {LeagueId})";
}
