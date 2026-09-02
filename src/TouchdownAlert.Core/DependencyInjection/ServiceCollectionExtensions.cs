using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Alerting;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Detection;
using TouchdownAlert.Core.Espn;
using TouchdownAlert.Core.Sounds;

namespace TouchdownAlert.Core.DependencyInjection;

/// <summary>Wires up the TouchdownAlert Core services: options binding, detector, alert router, sound resolver, ESPN source.</summary>
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

        services.AddOptions<EspnOptions>().Bind(configuration.GetSection(EspnOptions.SectionName));
        services.AddOptions<PollingOptions>().Bind(configuration.GetSection(PollingOptions.SectionName));
        services.AddOptions<SoundOptions>().Bind(configuration.GetSection(SoundOptions.SectionName));
        services.AddOptions<AlertOptions>().Bind(configuration.GetSection(AlertOptions.SectionName));

        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<ITouchdownDetector, TouchdownDetector>();
        services.AddSingleton<IAlertRouter, AlertRouter>();
        services.AddSingleton<ISoundFileResolver, SoundFileResolver>();

        services.AddHttpClient<ILeagueSource, EspnLeagueSource>((provider, client) =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<EspnOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        });

        return services;
    }
}
