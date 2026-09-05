using System.Text.Json;
using TouchdownAlert.Overlay.Contracts;

namespace TouchdownAlert.Overlay.Tests;

public class JsonDeserializationTests
{
    [Fact]
    public void StateDto_deserializes_camelCase_payload()
    {
        const string json = """
            {
              "leagueName": "Dunn League",
              "watchedTeams": [
                { "teamId": 1, "leagueKey": "main", "label": "Michael", "color": "#22c55e", "points": 92.8, "touchdownTotal": 3 },
                { "teamId": 3, "leagueKey": "main", "label": "Lauryn", "color": "#3b82f6", "points": null, "touchdownTotal": 0 }
              ]
            }
            """;

        var dto = JsonSerializer.Deserialize<StateDto>(json, JsonOptions.Default)!;

        Assert.Equal("Dunn League", dto.LeagueName);
        Assert.Equal(2, dto.WatchedTeams.Count);
        Assert.Equal(1, dto.WatchedTeams[0].TeamId);
        Assert.Equal(92.8, dto.WatchedTeams[0].Points);
        Assert.Null(dto.WatchedTeams[1].Points);
    }

    [Fact]
    public void StateDto_tolerates_missing_and_unknown_fields()
    {
        const string json = """{ "somethingElse": 42 }""";

        var dto = JsonSerializer.Deserialize<StateDto>(json, JsonOptions.Default)!;

        Assert.Null(dto.LeagueName);
        Assert.Empty(dto.WatchedTeams);
    }

    [Fact]
    public void AlertDto_deserializes_camelCase_payload()
    {
        const string json = """
            {
              "at": "2026-09-04T20:15:00Z",
              "teamId": 1,
              "leagueKey": "main",
              "teamLabel": "Michael",
              "playerName": "Josh Allen",
              "touchdownType": "Passing",
              "count": 2,
              "isTest": true
            }
            """;

        var dto = JsonSerializer.Deserialize<AlertDto>(json, JsonOptions.Default)!;

        Assert.Equal(1, dto.TeamId);
        Assert.Equal("main", dto.LeagueKey);
        Assert.Equal("Josh Allen", dto.PlayerName);
        Assert.Equal("Passing", dto.TouchdownType);
        Assert.Equal(2, dto.Count);
        Assert.True(dto.IsTest);
    }

    [Fact]
    public void AlertDto_defaults_count_to_one_and_is_test_to_false_when_missing()
    {
        const string json = """{ "teamId": 1, "playerName": "Josh Allen" }""";

        var dto = JsonSerializer.Deserialize<AlertDto>(json, JsonOptions.Default)!;

        Assert.Equal(1, dto.Count);
        Assert.False(dto.IsTest);
    }

    [Fact]
    public void SettingsDto_deserializes_overlay_sub_object()
    {
        const string json = """
            {
              "leagues": [],
              "overlay": { "x": 100.5, "y": 20, "display": "secondary", "scale": 1.2, "opacity": 0.8, "locked": false, "enabled": true }
            }
            """;

        var dto = JsonSerializer.Deserialize<SettingsDto>(json, JsonOptions.Default)!;

        Assert.NotNull(dto.Overlay);
        Assert.Equal(100.5, dto.Overlay!.X);
        Assert.Equal("secondary", dto.Overlay.Display);
        Assert.False(dto.Overlay.Locked);
    }

    [Fact]
    public void SettingsDto_overlay_is_null_when_absent_and_caller_can_fall_back_to_defaults()
    {
        const string json = """{ "leagues": [] }""";

        var dto = JsonSerializer.Deserialize<SettingsDto>(json, JsonOptions.Default)!;

        Assert.Null(dto.Overlay);
    }

    [Fact]
    public void OverlaySettingsDto_round_trips_through_serialize_deserialize()
    {
        var original = new OverlaySettingsDto { X = 10, Y = 20, Display = "primary", Scale = 1.0, Opacity = 0.7, Locked = true, Enabled = true };

        var json = JsonSerializer.Serialize(original, JsonOptions.Default);
        var roundTripped = JsonSerializer.Deserialize<OverlaySettingsDto>(json, JsonOptions.Default)!;

        Assert.Equal(original.X, roundTripped.X);
        Assert.Equal(original.Display, roundTripped.Display);
        Assert.Equal(original.Locked, roundTripped.Locked);
    }
}
