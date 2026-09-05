// Exercises the settings API (GET/PUT /api/settings, PUT /api/settings/overlay) that backs the control page
// and the overlay exe, hosting the real App in-process against a temp config/settings.json and a temp sounds
// folder (via the "Settings:FilePath" / "Sounds:Directory" UseSetting overrides Program.cs honors).
extern alias AppAssembly;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using AppProgram = AppAssembly::Program;

namespace TouchdownAlert.IntegrationTests;

public sealed class SettingsApiTests : IAsyncLifetime
{
    private string _tempDir = null!;
    private string _settingsFilePath = null!;
    private string _soundsDir = null!;
    private WebApplicationFactory<AppProgram> _factory = null!;
    private HttpClient _client = null!;

    private const string InitialFileJson = """
    {
      "Leagues": [
        { "Key": "main", "Provider": "Espn", "LeagueId": "123", "BaseUrl": "http://127.0.0.1:1" }
      ],
      "Alerts": {
        "WatchedTeams": [
          { "TeamId": 1, "League": "main", "Label": "Michael", "SoundFile": "airhorn.mp3", "Color": "#22c55e" }
        ]
      },
      "Polling": { "IntervalSeconds": 30 },
      "Sounds": { "Volume": 1, "MaxDurationSeconds": 5 },
      "Overlay": { "X": null, "Y": null, "Display": "primary", "Scale": 1, "Opacity": 0.7, "Locked": true, "Enabled": true }
    }
    """;

    public Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TouchdownAlert.SettingsApiTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _settingsFilePath = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(_settingsFilePath, InitialFileJson);

        _soundsDir = Path.Combine(_tempDir, "sounds");
        Directory.CreateDirectory(_soundsDir);
        File.WriteAllBytes(Path.Combine(_soundsDir, "airhorn.mp3"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(_soundsDir, "extra.wav"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(_soundsDir, "notes.txt"), Array.Empty<byte>()); // must not show up as a sound

        Environment.SetEnvironmentVariable("TOUCHDOWNALERT_SILENT", "1");

        _factory = new WebApplicationFactory<AppProgram>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Settings:FilePath", _settingsFilePath);
                builder.UseSetting("Sounds:Directory", _soundsDir);
                builder.UseSetting("Sounds:Enabled", "false");
                builder.UseSetting("Polling:IntervalSeconds", "600"); // avoid background poll noise during these tests
            });

        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Get_settings_returns_document_and_meta_with_available_sounds()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/settings");

        var leagues = body.GetProperty("leagues");
        Assert.Equal(1, leagues.GetArrayLength());
        Assert.Equal("main", leagues[0].GetProperty("key").GetString());
        Assert.Equal("123", leagues[0].GetProperty("leagueId").GetString());

        var watchedTeams = body.GetProperty("watchedTeams");
        Assert.Equal(1, watchedTeams.GetArrayLength());
        Assert.Equal("Michael", watchedTeams[0].GetProperty("label").GetString());

        var meta = body.GetProperty("meta");
        Assert.Equal(Path.GetFullPath(_settingsFilePath), Path.GetFullPath(meta.GetProperty("settingsFile").GetString()!));
        Assert.False(meta.GetProperty("restartRequired").GetBoolean());

        var availableSounds = meta.GetProperty("availableSounds").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(new[] { "airhorn.mp3", "extra.wav" }, availableSounds);
    }

    [Fact]
    public async Task Put_settings_with_invalid_color_returns_400_with_errors()
    {
        var doc = await GetSettingsNodeAsync();
        doc!["watchedTeams"]![0]!["color"] = "not-a-color";

        var response = await PutSettingsAsync(doc);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errorBody = await response.Content.ReadFromJsonAsync<JsonElement>();
        var errors = errorBody.GetProperty("errors").EnumerateArray().ToList();
        Assert.NotEmpty(errors);
    }

    [Fact]
    public async Task Put_settings_label_only_change_rewrites_file_and_does_not_require_restart()
    {
        var doc = await GetSettingsNodeAsync();
        doc!["watchedTeams"]![0]!["label"] = "Michael D";

        var response = await PutSettingsAsync(doc);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.False(result.GetProperty("restartRequired").GetBoolean());

        var onDisk = JsonNode.Parse(await File.ReadAllTextAsync(_settingsFilePath))!;
        Assert.Equal("Michael D", onDisk["Alerts"]!["WatchedTeams"]![0]!["Label"]!.GetValue<string>());
        // The file shape is preserved: Leagues at the top level, watched teams nested under Alerts.
        Assert.Equal("main", onDisk["Leagues"]![0]!["Key"]!.GetValue<string>());
    }

    [Fact]
    public async Task Put_settings_adding_a_league_requires_restart()
    {
        var doc = await GetSettingsNodeAsync();
        var newLeague = new JsonObject
        {
            ["key"] = "second",
            ["provider"] = "Espn",
            ["leagueId"] = "999",
            ["baseUrl"] = "http://127.0.0.1:1",
            ["seasonId"] = null,
            ["scoringPeriodId"] = null,
        };
        doc!["leagues"]!.AsArray().Add(newLeague);

        var response = await PutSettingsAsync(doc);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("restartRequired").GetBoolean());

        var getAfter = await _client.GetFromJsonAsync<JsonElement>("/api/settings");
        Assert.True(getAfter.GetProperty("meta").GetProperty("restartRequired").GetBoolean());
    }

    [Fact]
    public async Task Put_overlay_merges_without_touching_leagues_or_watched_teams()
    {
        var overlay = new JsonObject
        {
            ["x"] = 100.0,
            ["y"] = 200.0,
            ["display"] = "secondary",
            ["scale"] = 1.25,
            ["opacity"] = 0.5,
            ["locked"] = false,
            ["enabled"] = true,
        };

        var response = await _client.PutAsync("/api/settings/overlay", JsonContent(overlay));
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal("secondary", result.GetProperty("overlay").GetProperty("display").GetString());

        var onDisk = JsonNode.Parse(await File.ReadAllTextAsync(_settingsFilePath))!;
        Assert.Equal("secondary", onDisk["Overlay"]!["Display"]!.GetValue<string>());
        Assert.Equal("main", onDisk["Leagues"]![0]!["Key"]!.GetValue<string>());
        Assert.Equal("Michael", onDisk["Alerts"]!["WatchedTeams"]![0]!["Label"]!.GetValue<string>());
    }

    [Fact]
    public async Task Put_settings_label_change_is_reflected_in_state_within_a_second()
    {
        var doc = await GetSettingsNodeAsync();
        doc!["watchedTeams"]![0]!["label"] = "Renamed Live";

        (await PutSettingsAsync(doc)).EnsureSuccessStatusCode();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        string? label = null;
        while (DateTime.UtcNow < deadline)
        {
            var state = await _client.GetFromJsonAsync<JsonElement>("/api/state");
            var teams = state.GetProperty("watchedTeams").EnumerateArray().ToList();
            if (teams.Count > 0)
            {
                label = teams[0].GetProperty("label").GetString();
                if (label == "Renamed Live")
                {
                    break;
                }
            }

            await Task.Delay(100);
        }

        Assert.Equal("Renamed Live", label);
    }

    private async Task<JsonNode?> GetSettingsNodeAsync()
    {
        var json = await _client.GetStringAsync("/api/settings");
        var full = JsonNode.Parse(json)!;
        // PUT /api/settings takes the document without "meta".
        full.AsObject().Remove("meta");
        return full;
    }

    private Task<HttpResponseMessage> PutSettingsAsync(JsonNode? body) => _client.PutAsync("/api/settings", JsonContent(body));

    private static StringContent JsonContent(JsonNode? node) =>
        new(node?.ToJsonString(new JsonSerializerOptions()) ?? "null", Encoding.UTF8, "application/json");
}
