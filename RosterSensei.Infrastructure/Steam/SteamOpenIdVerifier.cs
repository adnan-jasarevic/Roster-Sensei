using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RosterSensei.Application.Common;
using RosterSensei.Application.Interfaces;

namespace RosterSensei.Infrastructure.Steam;

// Steam's OpenID 2.0 provider, used in stateless mode: every reply is checked by asking Steam directly
// (check_authentication), so no shared secrets are kept here.
public sealed partial class SteamOpenIdVerifier : ISteamOpenIdVerifier
{
    public const string HttpClientName = "steam-openid";
    public const string Endpoint = "https://steamcommunity.com/openid/login";

    private const string Namespace = "http://specs.openid.net/auth/2.0";
    private const string IdentifierSelect = "http://specs.openid.net/auth/2.0/identifier_select";

    // Fields that must be covered by Steam's signature, or a reply could be edited without breaking it.
    private static readonly string[] RequiredSigned = ["op_endpoint", "claimed_id", "identity", "return_to", "response_nonce", "assoc_handle"];

    [GeneratedRegex(@"^https://steamcommunity\.com/openid/id/(\d{17})$")]
    private static partial Regex ClaimedIdPattern();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SteamOpenIdVerifier> _logger;

    public SteamOpenIdVerifier(IHttpClientFactory httpClientFactory, ILogger<SteamOpenIdVerifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string BuildSignInUrl(string returnTo, string realm)
    {
        var query = new Dictionary<string, string>
        {
            ["openid.ns"] = Namespace,
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = returnTo,
            ["openid.realm"] = realm,
            ["openid.identity"] = IdentifierSelect,
            ["openid.claimed_id"] = IdentifierSelect
        };
        return Endpoint + "?" + string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    public async Task<string?> VerifyAsync(IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        var steamId = ReadClaimedSteamId(parameters);
        if (steamId == null)
            return null;

        var form = parameters
            .Where(kv => kv.Key.StartsWith("openid.", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        form["openid.mode"] = "check_authentication";

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var content = new FormUrlEncodedContent(form);
            using var response = await client.PostAsync(Endpoint, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Steam OpenID check returned HTTP {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            // Key-value form: one "key:value" per line. Steam answers is_valid:true only once per reply.
            var isValid = body.Split('\n')
                .Select(line => line.Trim())
                .Any(line => line == "is_valid:true");
            return isValid ? steamId : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Steam OpenID check failed.");
            return null;
        }
    }

    // The SteamID64 from a reply that is shaped like a genuine Steam reply, or null.
    public static string? ReadClaimedSteamId(IReadOnlyDictionary<string, string> parameters)
    {
        string? Get(string key) => parameters.TryGetValue("openid." + key, out var value) ? value : null;

        if (Get("ns") != Namespace || Get("mode") != "id_res" || Get("op_endpoint") != Endpoint)
            return null;

        var claimedId = Get("claimed_id");
        if (claimedId == null || claimedId != Get("identity"))
            return null;

        var signed = (Get("signed") ?? string.Empty).Split(',');
        if (RequiredSigned.Any(field => !signed.Contains(field)) || signed.Any(field => Get(field) == null))
            return null;
        if (string.IsNullOrEmpty(Get("sig")))
            return null;

        var match = ClaimedIdPattern().Match(claimedId);
        return match.Success && SteamId.TryParse(match.Groups[1].Value, out var steamId64) ? steamId64 : null;
    }
}
