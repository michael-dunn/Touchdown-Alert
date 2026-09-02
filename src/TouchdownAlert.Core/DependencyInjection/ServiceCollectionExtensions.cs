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

        services.AddSingleton(TimeProvider.System);

        // Fail fast at startup: unique/non-empty league keys, resolvable watched-team leagues, Yahoo unsupported.
        var leagues = new LeaguesOptions();
        configuration.GetSection(LeaguesOptions.SectionName).Bind(leagues.Items);
        var alertOptions = new AlertOptions();
        configuration.GetSection(AlertOptions.SectionName).Bind(alertOptions);
        LeagueConfigurationValidator.ValidateAndResolve(leagues.Items, alertOptions.WatchedTeams);

        services.AddSingleton<ITouchdownDetector, TouchdownDetector>();
        services.AddSingleton<IAlertRouter, AlertRouter>();
        services.AddSingleton<ISoundFileResolver, SoundFileResolver>();

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

        services.AddSingleton<IEnumerable<ILeagueSource>>(provider =>
        {
            var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
            var timeProvider = provider.GetRequiredService<TimeProvider>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var currentLeagues = provider.GetRequiredService<IOptions<LeaguesOptions>>().Value.Items;

            var sources = new List<ILeagueSource>();
            foreach (var leagueOptions in currentLeagues)
            {
                switch (leagueOptions.Provider)
                {
                    case LeagueProvider.Espn:
                        var client = httpClientFactory.CreateClient(HttpClientName(leagueOptions.Key));
                        sources.Add(new EspnLeagueSource(
                            client,
                            leagueOptions,
                            timeProvider,
                            loggerFactory.CreateLogger<EspnLeagueSource>()));
                        break;

                    case LeagueProvider.Yahoo:
                        throw new NotSupportedException("Yahoo leagues are not supported yet; coming soon");

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
