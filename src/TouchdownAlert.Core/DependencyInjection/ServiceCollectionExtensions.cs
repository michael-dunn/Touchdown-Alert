using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Alerting;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Detection;
using TouchdownAlert.Core.Espn;
using TouchdownAlert.Core.Models;
using TouchdownAlert.Core.Sounds;
using TouchdownAlert.Core.Yahoo;

namespace TouchdownAlert.Core.DependencyInjection;

/// <summary>Wires up the TouchdownAlert Core services: options binding, detector, alert router, sound resolver, league sources.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything in TouchdownAlert.Core against the given configuration root. Core has no ASP.NET or
    /// audio dependencies, so the host (App) still owns hosting, the dashboard, and actual audio playback
    /// (<see cref="Abstractions.ISoundPlayer"/> is not registered here).
    /// </summary>
    public static IServiceCollection AddTouchdownAlertCore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<LeaguesOptions>().Configure(o => configuration.GetSection(LeaguesOptions.SectionName).Bind(o.Items));
        services.AddOptions<PollingOptions>().Bind(configuration.GetSection(PollingOptions.SectionName));
        services.AddOptions<SoundOptions>().Bind(configuration.GetSection(SoundOptions.SectionName));
        services.AddOptions<AlertOptions>().Bind(configuration.GetSection(AlertOptions.SectionName));
        services.AddOptions<YahooOptions>().Bind(configuration.GetSection(YahooOptions.SectionName));
        services.Configure<OverlayOptions>(configuration.GetSection(OverlayOptions.SectionName));

        services.AddSingleton(TimeProvider.System);

        // Fail fast at startup: unique/non-empty league keys, resolvable watched-team leagues, and (for any
        // Yahoo league) a configured ClientId/ClientSecret. NOT being logged in to Yahoo is deliberately not
        // checked here - see YahooAuthException.
        var leagues = new LeaguesOptions();
        configuration.GetSection(LeaguesOptions.SectionName).Bind(leagues.Items);
        var alertOptions = new AlertOptions();
        configuration.GetSection(AlertOptions.SectionName).Bind(alertOptions);
        var yahooOptions = new YahooOptions();
        configuration.GetSection(YahooOptions.SectionName).Bind(yahooOptions);
        LeagueConfigurationValidator.ValidateAndResolve(leagues.Items, alertOptions.WatchedTeams, yahooOptions);

        services.AddSingleton<ITouchdownDetector, TouchdownDetector>();
        services.AddSingleton<IAlertRouter, AlertRouter>();
        services.AddSingleton<ISoundFileResolver, SoundFileResolver>();

        services.AddSingleton<IYahooTokenStore, YahooTokenStore>();
        services.AddHttpClient("yahoo-auth");
        services.AddSingleton<IYahooAuthService>(provider => new YahooAuthService(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient("yahoo-auth"),
            provider.GetRequiredService<IOptionsMonitor<YahooOptions>>(),
            provider.GetRequiredService<IYahooTokenStore>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<YahooAuthService>()));

        // Register a named HttpClient per configured league so each ILeagueSource gets its own base
        // address/timeout, keyed the same way the ILeagueSource factory below looks them up.
        foreach (var leagueOptions in leagues.Items.Where(l => l.Provider == LeagueProvider.Espn))
        {
            services.AddHttpClient(HttpClientName(leagueOptions.Key), client =>
            {
                client.BaseAddress = new Uri(leagueOptions.BaseUrl ?? EspnLeagueSource.DefaultBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(leagueOptions.RequestTimeoutSeconds);
            });
        }

        foreach (var leagueOptions in leagues.Items.Where(l => l.Provider == LeagueProvider.Yahoo))
        {
            services.AddHttpClient(HttpClientName(leagueOptions.Key), (provider, client) =>
            {
                var apiBaseUrl = provider.GetRequiredService<IOptionsMonitor<YahooOptions>>().CurrentValue.ApiBaseUrl;
                client.BaseAddress = new Uri(leagueOptions.BaseUrl ?? apiBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(leagueOptions.RequestTimeoutSeconds);
            });
        }

        services.AddSingleton<IEnumerable<ILeagueSource>>(provider =>
        {
            var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
            var timeProvider = provider.GetRequiredService<TimeProvider>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var currentLeagues = provider.GetRequiredService<IOptions<LeaguesOptions>>().Value.Items;
            var alertOptionsMonitor = provider.GetRequiredService<IOptionsMonitor<AlertOptions>>();
            var yahooAuth = provider.GetRequiredService<IYahooAuthService>();

            var sources = new List<ILeagueSource>();
            foreach (var leagueOptions in currentLeagues)
            {
                switch (leagueOptions.Provider)
                {
                    case LeagueProvider.Espn:
                        var espnClient = httpClientFactory.CreateClient(HttpClientName(leagueOptions.Key));
                        sources.Add(new EspnLeagueSource(
                            espnClient,
                            leagueOptions,
                            timeProvider,
                            loggerFactory.CreateLogger<EspnLeagueSource>()));
                        break;

                    case LeagueProvider.Yahoo:
                        var yahooClient = httpClientFactory.CreateClient(HttpClientName(leagueOptions.Key));
                        sources.Add(new YahooLeagueSource(
                            yahooClient,
                            leagueOptions,
                            yahooAuth,
                            alertOptionsMonitor,
                            timeProvider,
                            loggerFactory.CreateLogger<YahooLeagueSource>()));
                        break;

                    default:
                        throw new NotSupportedException($"Unknown league provider \"{leagueOptions.Provider}\".");
                }
            }

            return sources;
        });

        return services;
    }

    private static string HttpClientName(string leagueKey) => "league:" + leagueKey;
}
