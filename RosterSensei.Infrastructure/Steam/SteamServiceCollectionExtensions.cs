using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RosterSensei.Application.Common;
using RosterSensei.Application.Interfaces;

namespace RosterSensei.Infrastructure.Steam;

public static class SteamServiceCollectionExtensions
{
    // Steam sign-in plus names and avatars. allowedOrigins are the frontend origins Steam may return to.
    // Nothing here needs a Steam Web API key.
    public static IServiceCollection AddSteam(this IServiceCollection services, IEnumerable<string> allowedOrigins)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton(new SteamSignInSettings(allowedOrigins));
        services.AddSingleton<SteamSignInStates>();

        // Short timeouts: a slow Steam must not hold a request (and its DB connection) for long.
        // Response limits: Steam's sign-in replies are tiny; a public profile page is tens of KB.
        services.AddHttpClient(SteamOpenIdVerifier.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.MaxResponseContentBufferSize = 64 * 1024;
        });
        services.AddHttpClient(SteamCommunityProfileProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
            client.MaxResponseContentBufferSize = 1024 * 1024;
        });

        services.AddSingleton<ISteamOpenIdVerifier, SteamOpenIdVerifier>();
        services.AddSingleton<ISteamProfileProvider, SteamCommunityProfileProvider>();
        return services;
    }
}
