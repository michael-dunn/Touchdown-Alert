using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.DependencyInjection;
using TouchdownAlert.Core.Models;
using TouchdownAlert.Core.Sleeper;
using static TouchdownAlert.Core.Tests.SleeperTestSupport;

namespace TouchdownAlert.Core.Tests;

/// <summary>
/// Wires a Sleeper league through <see cref="ServiceCollectionExtensions.AddTouchdownAlertCore"/> exactly the
/// way the App and the integration tests do, including the tests' trick of routing every HttpClient at a fake
/// primary handler via ConfigureHttpClientDefaults AFTER Core registered its own decompressing handler.
/// </summary>
public class SleeperDependencyInjectionTests : IDisposable
{
    private readonly string _tempDir = NewTempDirectory();

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private ServiceProvider Build(RoutingHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Leagues:0:Key"] = "sleeper",
            ["Leagues:0:Provider"] = "Sleeper",
            ["Leagues:0:LeagueId"] = LeagueId,
            ["Alerts:WatchedTeams:0:TeamId"] = "1",
            ["Alerts:WatchedTeams:0:SoundFile"] = "a.mp3",
            ["Sleeper:PlayersCacheFilePath"] = Path.Combine(_tempDir, "players.json"),
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTouchdownAlertCore(configuration);
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AddTouchdownAlertCore_SleeperLeague_ResolvesSourceRoutedThroughDefaultHandler()
    {
        var handler = new RoutingHandler(RouteFixtures);
        await using var provider = Build(handler);

        var sources = provider.GetRequiredService<IEnumerable<ILeagueSource>>().ToList();
        var source = Assert.Single(sources);
        Assert.IsType<SleeperLeagueSource>(source);
        Assert.Equal(LeagueProvider.Sleeper, source.League.Provider);

        var snapshot = await source.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal(12, snapshot.Teams.Count);
        Assert.Equal("Detroit Lions", snapshot.FindTeam(1)!.Roster.Single(p => p.Position == "D/ST").FullName);
        // Every request - the per-league client's and the shared directory's - went through the test handler.
        Assert.Equal(1, handler.CountRequests("/v1/players/nfl"));
        Assert.Contains("/v1/state/nfl", handler.RequestPaths);
        Assert.True(File.Exists(Path.Combine(_tempDir, "players.json")));
    }

    [Fact]
    public async Task AddTouchdownAlertCore_SleeperLeague_WithoutYahooCredentials_Passes()
    {
        await using var provider = Build(new RoutingHandler(RouteFixtures));

        Assert.NotNull(provider.GetRequiredService<ISleeperPlayerDirectory>());
    }
}
