using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RosterSensei.Application.Common;
using RosterSensei.Application.Interfaces;

namespace RosterSensei.Infrastructure.Steam;

public static class SteamServiceCollectionExtensions
{
    // Steam sign-in plus names and avatars. allowedOrigins are the frontend origins Steam may return to.
    public static IServiceCollection AddSteam(this IServiceCollection services, IConfiguration configuration, IEnumerable<string> allowedOrigins)
    {
        services.Configure<SteamOptions>(configuration.GetSection(SteamOptions.Section));
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton(new SteamSignInSettings(allowedOrigins));
        services.AddSingleton<SteamSignInStates>();

        // Short timeouts: a slow Steam must not hold a request (and its DB connection) for long.
        // Small response limits: Steam's replies are a few KB at most.
        services.AddHttpClient(SteamOpenIdVerifier.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.MaxResponseContentBufferSize = 64 * 1024;
        });
        services.AddHttpClient(SteamWebApiProfileProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
            client.MaxResponseContentBufferSize = 1024 * 1024;
        });

        services.AddSingleton<ISteamOpenIdVerifier, SteamOpenIdVerifier>();
        services.AddSingleton<ISteamProfileProvider, SteamWebApiProfileProvider>();
        return services;
    }
}
