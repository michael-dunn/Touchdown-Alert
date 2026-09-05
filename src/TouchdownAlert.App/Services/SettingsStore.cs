using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TouchdownAlert.App.Contracts;
using TouchdownAlert.Core.Configuration;

namespace TouchdownAlert.App.Services;

/// <summary>
/// Thread-safe reader/writer of config/settings.json - the only place a running TouchdownAlert instance's
/// leagues, watched teams, polling interval, sound volume/duration, and overlay position/look are edited.
/// Validates edits with <see cref="LeagueConfigurationValidator"/> plus the extra rules the control page and
/// overlay exe need (max 4 watched teams, hex colors, overlay scale/opacity, poll interval range) and writes
/// atomically (temp file + move) so a crash mid-write can't corrupt the file.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions FileJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Regex HexColor = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    private readonly object _lock = new();
    private readonly IOptionsMonitor<YahooOptions> _yahooOptions;
    private readonly HashSet<(string Key, string Provider, string LeagueId)> _startupLeagueKeys;

    public SettingsStore(string filePath, IReadOnlyList<LeagueOptions> startupLeagues, IOptionsMonitor<YahooOptions> yahooOptions)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(startupLeagues);
        ArgumentNullException.ThrowIfNull(yahooOptions);

        FilePath = filePath;
        _yahooOptions = yahooOptions;
        _startupLeagueKeys = startupLeagues.Select(KeyOf).ToHashSet();
    }

    /// <summary>Absolute path to config/settings.json (or the overridden <c>Settings:FilePath</c> in tests).</summary>
    public string FilePath { get; }

    /// <summary>Reads the current document from disk. Missing file (or missing sections within it) yields
    /// empty/default values rather than throwing - the app must start (and this must read cleanly) with zero
    /// leagues configured.</summary>
    public SettingsDocument Read()
    {
        lock (_lock)
        {
            if (!File.Exists(FilePath))
            {
                return EmptyDocument();
            }

            FileShape? file;
            try
            {
                file = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(FilePath), FileJsonOptions);
            }
            catch (JsonException)
            {
                file = null;
            }

            file ??= new FileShape(null, null, null, null, null);

            return new SettingsDocument(
                file.Leagues ?? new List<LeagueOptions>(),
                file.Alerts?.WatchedTeams ?? new List<WatchedTeamOptions>(),
                file.Polling ?? new PollingOptions(),
                file.Sounds ?? new SettingsSoundsDto(1f, 5),
                file.Overlay ?? new OverlayOptions());
        }
    }

    /// <summary>True when the set of (Key, Provider, LeagueId) tuples differs from what the process started
    /// with - adding/removing a league or changing its provider/id needs a restart to take effect.</summary>
    public bool IsRestartRequired(IReadOnlyList<LeagueOptions>? leaguesOverride = null)
    {
        var leagues = leaguesOverride ?? Read().Leagues;
        return !leagues.Select(KeyOf).ToHashSet().SetEquals(_startupLeagueKeys);
    }

    /// <summary>Validates a candidate document, returning every problem found (empty = valid). Never throws.</summary>
    public List<string> Validate(SettingsDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var errors = new List<string>();

        try
        {
            // Validate against a copy: ValidateAndResolve mutates blank League fields in place, and we don't
            // want a failed validation to have side-effected the caller's document.
            var watchedCopy = doc.WatchedTeams
                .Select(w => new WatchedTeamOptions { TeamId = w.TeamId, League = w.League, Label = w.Label, SoundFile = w.SoundFile, Color = w.Color })
                .ToList();
            LeagueConfigurationValidator.ValidateAndResolve(doc.Leagues, watchedCopy, _yahooOptions.CurrentValue);
        }
        catch (InvalidOperationException ex)
        {
            errors.Add(ex.Message);
        }

        if (doc.WatchedTeams.Count > 4)
        {
            errors.Add($"At most 4 watched teams are supported ({doc.WatchedTeams.Count} configured).");
        }

        foreach (var watched in doc.WatchedTeams)
        {
            if (!string.IsNullOrWhiteSpace(watched.Color) && !HexColor.IsMatch(watched.Color))
            {
                errors.Add($"Team {watched.TeamId}: color \"{watched.Color}\" must look like #rrggbb.");
            }
        }

        if (doc.Overlay.Scale is < 0.5 or > 2.0)
        {
            errors.Add("Overlay scale must be between 0.5 and 2.0.");
        }

        if (doc.Overlay.Opacity is < 0.0 or > 1.0)
        {
            errors.Add("Overlay opacity must be between 0.0 and 1.0.");
        }

        if (doc.Polling.IntervalSeconds is < 5 or > 600)
        {
            errors.Add("Polling interval must be between 5 and 600 seconds.");
        }

        return errors;
    }

    /// <summary>Validates and, if valid, atomically rewrites config/settings.json with the whole document.</summary>
    public (bool Ok, List<string> Errors, bool RestartRequired) Write(SettingsDocument doc)
    {
        var errors = Validate(doc);
        if (errors.Count > 0)
        {
            return (false, errors, false);
        }

        lock (_lock)
        {
            var restartRequired = IsRestartRequired(doc.Leagues);
            WriteFile(doc);
            return (true, errors, restartRequired);
        }
    }

    /// <summary>Validates and merges just the overlay section into the file, leaving everything else untouched.
    /// Used by the overlay exe (saving a dragged position) and the control page (lock/scale/opacity toggles).</summary>
    public (bool Ok, List<string> Errors, OverlayOptions Overlay) WriteOverlay(OverlayOptions overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var errors = new List<string>();

        if (overlay.Scale is < 0.5 or > 2.0)
        {
            errors.Add("Overlay scale must be between 0.5 and 2.0.");
        }

        if (overlay.Opacity is < 0.0 or > 1.0)
        {
            errors.Add("Overlay opacity must be between 0.0 and 1.0.");
        }

        if (errors.Count > 0)
        {
            return (false, errors, overlay);
        }

        lock (_lock)
        {
            var current = Read();
            var merged = current with { Overlay = overlay };
            WriteFile(merged);
            return (true, errors, overlay);
        }
    }

    private void WriteFile(SettingsDocument doc)
    {
        var payload = new
        {
            Leagues = doc.Leagues,
            Alerts = new { WatchedTeams = doc.WatchedTeams },
            Polling = doc.Polling,
            Sounds = doc.Sounds,
            Overlay = doc.Overlay,
        };

        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = FilePath + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(tempPath, JsonSerializer.Serialize(payload, FileJsonOptions));
        File.Move(tempPath, FilePath, overwrite: true);
    }

    private static SettingsDocument EmptyDocument() => new(
        new List<LeagueOptions>(),
        new List<WatchedTeamOptions>(),
        new PollingOptions(),
        new SettingsSoundsDto(1f, 5),
        new OverlayOptions());

    private static (string, string, string) KeyOf(LeagueOptions league) => (league.Key, league.Provider.ToString(), league.LeagueId);

    private sealed record FileShape(List<LeagueOptions>? Leagues, FileAlerts? Alerts, PollingOptions? Polling, SettingsSoundsDto? Sounds, OverlayOptions? Overlay);

    private sealed record FileAlerts(List<WatchedTeamOptions>? WatchedTeams);
}
