using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace OrderAggregator.Api.Authentication;

/// <summary>
/// Authentication handler that validates the configured request header against
/// the list of accepted keys. Comparison is constant-time to avoid leaking key
/// material via timing side-channels.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IOptionsMonitor<ApiKeyOptions> _apiKeyOptions;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        IOptionsMonitor<ApiKeyOptions> apiKeyOptions)
        : base(options, loggerFactory, encoder)
    {
        _apiKeyOptions = apiKeyOptions;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var apiKeyOptions = _apiKeyOptions.CurrentValue;

        if (!Request.Headers.TryGetValue(apiKeyOptions.HeaderName, out var headerValues))
        {
            // No header at all → let the handler fall through; the
            // authorization stage will return 401 with the challenge.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var provided = headerValues.ToString();
        if (string.IsNullOrEmpty(provided))
        {
            return Task.FromResult(AuthenticateResult.Fail("Empty API key."));
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        foreach (var entry in apiKeyOptions.Keys)
        {
            var expectedBytes = Encoding.UTF8.GetBytes(entry.Key);
            if (CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
            {
                var identity = new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.Name, entry.Name),
                        new Claim("api_key_name", entry.Name),
                    },
                    Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);

                Logger.LogDebug("API key authenticated for client {ClientName}", entry.Name);
                return Task.FromResult(AuthenticateResult.Success(ticket));
            }
        }

        Logger.LogWarning("Rejected request with invalid API key from {RemoteIp}", Context.Connection.RemoteIpAddress);
        return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers["WWW-Authenticate"] = $"ApiKey realm=\"OrderAggregator\", header=\"{_apiKeyOptions.CurrentValue.HeaderName}\"";
        return Task.CompletedTask;
    }
}
