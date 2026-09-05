using System.Text;
using Microsoft.Extensions.Configuration;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Models;

namespace TouchdownAlert.Core.Tests;

public class LeagueConfigurationTests
{
    private static IConfigurationRoot BuildConfig(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder().AddJsonStream(stream).Build();
    }

    [Fact]
    public void Leagues_BindsAsArray()
    {
        var config = BuildConfig("""
        {
          "Leagues": [
            { "Key": "main", "Provider": "Espn", "LeagueId": "111", "BaseUrl": "http://localhost:5199" },
            { "Key": "other", "Provider": "Espn", "LeagueId": "222" }
          ]
        }
        """);

        var leagues = new LeaguesOptions();
        config.GetSection(LeaguesOptions.SectionName).Bind(leagues.Items);

        Assert.Equal(2, leagues.Items.Count);
        Assert.Equal("main", leagues.Items[0].Key);
        Assert.Equal(LeagueProvider.Espn, leagues.Items[0].Provider);
        Assert.Equal("111", leagues.Items[0].LeagueId);
        Assert.Equal("http://localhost:5199", leagues.Items[0].BaseUrl);
        Assert.Equal("other", leagues.Items[1].Key);
        Assert.Null(leagues.Items[1].BaseUrl);
    }

    [Fact]
    public void ValidateAndResolve_SingleLeague_FillsBlankWatchedTeamLeague()
    {
        var leagues = new List<LeagueOptions> { new() { Key = "main", Provider = LeagueProvider.Espn, LeagueId = "1" } };
        var watched = new List<WatchedTeamOptions> { new() { TeamId = 1, League = null, SoundFile = "a.mp3" } };

        LeagueConfigurationValidator.ValidateAndResolve(leagues, watched);

        Assert.Equal("main", watched[0].League);
    }

    [Fact]
    public void ValidateAndResolve_MultipleLeagues_BlankWatchedTeamLeague_Throws()
    {
        var leagues = new List<LeagueOptions>
        {
            new() { Key = "main", Provider = LeagueProvider.Espn, LeagueId = "1" },
            new() { Key = "other", Provider = LeagueProvider.Espn, LeagueId = "2" },
        };
        var watched = new List<WatchedTeamOptions> { new() { TeamId = 1, League = null, SoundFile = "a.mp3" } };

        Assert.Throws<InvalidOperationException>(() => LeagueConfigurationValidator.ValidateAndResolve(leagues, watched));
    }

    [Fact]
    public void ValidateAndResolve_DuplicateLeagueKey_Throws()
    {
        var leagues = new List<LeagueOptions>
        {
            new() { Key = "main", Provider = LeagueProvider.Espn, LeagueId = "1" },
            new() { Key = "main", Provider = LeagueProvider.Espn, LeagueId = "2" },
        };

        Assert.Throws<InvalidOperationException>(() => LeagueConfigurationValidator.ValidateAndResolve(leagues, []));
    }

    [Fact]
    public void ValidateAndResolve_UnresolvableWatchedTeamLeague_Throws()
    {
        var leagues = new List<LeagueOptions> { new() { Key = "main", Provider = LeagueProvider.Espn, LeagueId = "1" } };
        var watched = new List<WatchedTeamOptions> { new() { TeamId = 1, League = "nope", SoundFile = "a.mp3" } };

        Assert.Throws<InvalidOperationException>(() => LeagueConfigurationValidator.ValidateAndResolve(leagues, watched));
    }

    [Fact]
    public void ValidateAndResolve_YahooProvider_WithoutClientCredentials_Throws()
    {
        var leagues = new List<LeagueOptions> { new() { Key = "main", Provider = LeagueProvider.Yahoo, LeagueId = "1" } };

        Assert.Throws<InvalidOperationException>(() => LeagueConfigurationValidator.ValidateAndResolve(leagues, []));
        Assert.Throws<InvalidOperationException>(() => LeagueConfigurationValidator.ValidateAndResolve(
            leagues, [], new YahooOptions { ClientId = "", ClientSecret = "" }));
        Assert.Throws<InvalidOperationException>(() => LeagueConfigurationValidator.ValidateAndResolve(
            leagues, [], new YahooOptions { ClientId = "id-only", ClientSecret = "" }));
    }

    [Fact]
    public void ValidateAndResolve_YahooProvider_WithClientCredentials_Passes()
    {
        var leagues = new List<LeagueOptions> { new() { Key = "main", Provider = LeagueProvider.Yahoo, LeagueId = "1" } };

        LeagueConfigurationValidator.ValidateAndResolve(leagues, [], new YahooOptions { ClientId = "id", ClientSecret = "secret" });
    }

    [Fact]
    public void ValidateAndResolve_NoLeaguesConfigured_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => LeagueConfigurationValidator.ValidateAndResolve([], []));
    }
}
