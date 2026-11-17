using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using mcp_todo.Exceptions;
using mcp_todo.Models;

namespace mcp_todo.Services;

public class DescopeAuthService : IDescopeAuthService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DescopeAuthService>? _logger;
    private readonly string _projectId;
    private readonly string _appId;
    private readonly string _configUrl;
    private readonly IConfigurationManager<OpenIdConnectConfiguration> _configurationManager;
    private readonly JwtSecurityTokenHandler _tokenHandler;

    public DescopeAuthService(
        IConfiguration configuration,
        ILogger<DescopeAuthService>? logger = null,
        IConfigurationManager<OpenIdConnectConfiguration>? configurationManager = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger;
        _tokenHandler = new JwtSecurityTokenHandler();

        _projectId = _configuration["Descope:ProjectId"]
            ?? _configuration["Descope__ProjectId"]
            ?? Environment.GetEnvironmentVariable("DESCOPE_PROJECT_ID")
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_projectId))
        {
            throw new InvalidOperationException("Descope:ProjectId is not configured.");
        }

        _appId = _configuration["Descope:AppId"]
            ?? _configuration["Descope__AppId"]
            ?? Environment.GetEnvironmentVariable("DESCOPE_APP_ID")
            ?? string.Empty;

        _configUrl = _configuration["Descope:ConfigUrl"]
            ?? _configuration["Descope__ConfigUrl"]
            ?? Environment.GetEnvironmentVariable("DESCOPE_CONFIG_URL")
            ?? string.Empty;

        var metadataAddress = !string.IsNullOrWhiteSpace(_configUrl)
            ? _configUrl
            : (!string.IsNullOrWhiteSpace(_appId)
                ? $"https://api.euc1.descope.com/v1/apps/agentic/{_projectId}/{_appId}/.well-known/openid-configuration"
                : (_projectId.StartsWith("Peuc1", StringComparison.OrdinalIgnoreCase)
                    ? $"https://api.euc1.descope.com/{_projectId}/.well-known/jwks.json"
                    : $"https://api.descope.com/{_projectId}/.well-known/jwks.json"));

        _configurationManager = configurationManager
            ?? new ConfigurationManager<OpenIdConnectConfiguration>(
                metadataAddress,
                new DescopeJwksConfigurationRetriever(),
                new HttpDocumentRetriever());
    }

    public async Task<DescopeUserData> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new UnauthorizedException("Token is missing or empty.");
        }

        OpenIdConnectConfiguration openIdConfig;
        try
        {
            openIdConfig = await _configurationManager.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DescopeAuthService] Failed to retrieve JWKS metadata: {ex.Message}");
            _logger?.LogError(ex, "Failed to retrieve Descope JWKS configuration.");
            throw new UnauthorizedException("Invalid or expired Descope token.", ex);
        }

        var serverUrl = _configuration["SERVER_URL"]
            ?? Environment.GetEnvironmentVariable("SERVER_URL")
            ?? string.Empty;

        var validIssuers = new List<string>
        {
            $"https://api.descope.com/{_projectId}",
            $"https://api.euc1.descope.com/{_projectId}",
            _projectId
        };

        if (!string.IsNullOrWhiteSpace(_appId))
        {
            validIssuers.Add($"https://api.descope.com/v1/apps/agentic/{_projectId}/{_appId}");
            validIssuers.Add($"https://api.euc1.descope.com/v1/apps/agentic/{_projectId}/{_appId}");
            validIssuers.Add($"https://api.descope.com/oauth2/v1/apps/agentic/{_projectId}/{_appId}");
            validIssuers.Add($"https://api.euc1.descope.com/oauth2/v1/apps/agentic/{_projectId}/{_appId}");
        }

        if (!string.IsNullOrWhiteSpace(serverUrl))
        {
            var cleanServerUrl = serverUrl.TrimEnd('/');
            validIssuers.Add(cleanServerUrl);
            validIssuers.Add($"{cleanServerUrl}/mcp");
        }

        if (!string.IsNullOrWhiteSpace(openIdConfig.Issuer) && !validIssuers.Contains(openIdConfig.Issuer))
        {
            validIssuers.Add(openIdConfig.Issuer);
        }

        var validAudiences = new List<string> { _projectId };
        if (!string.IsNullOrWhiteSpace(_appId) && !validAudiences.Contains(_appId))
        {
            validAudiences.Add(_appId);
        }
        if (!string.IsNullOrWhiteSpace(serverUrl))
        {
            var cleanServerUrl = serverUrl.TrimEnd('/');
            validAudiences.Add(cleanServerUrl);
            validAudiences.Add($"{cleanServerUrl}/mcp");
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = openIdConfig.SigningKeys,
            ValidateIssuer = true,
            ValidIssuers = validIssuers,
            ValidateAudience = true,
            ValidAudiences = validAudiences,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
            AudienceValidator = (audiences, securityToken, parameters) =>
            {
                if (audiences == null || !audiences.Any()) return true;
                foreach (var aud in audiences)
                {
                    if (validAudiences.Contains(aud, StringComparer.OrdinalIgnoreCase) ||
                        aud.Contains(_projectId, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(_appId) && aud.Contains(_appId, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(serverUrl) && aud.Contains(serverUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
                return true;
            },
            IssuerValidator = (issuer, securityToken, parameters) =>
            {
                if (string.IsNullOrWhiteSpace(issuer))
                    throw new SecurityTokenInvalidIssuerException("Token has no issuer.");

                if (validIssuers.Contains(issuer, StringComparer.OrdinalIgnoreCase) ||
                    issuer.Contains("descope.com", StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(serverUrl) && issuer.StartsWith(serverUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) ||
                    issuer.Contains(_projectId, StringComparison.OrdinalIgnoreCase))
                {
                    return issuer;
                }

                throw new SecurityTokenInvalidIssuerException($"Issuer '{issuer}' is not valid.");
            }
        };

        ClaimsPrincipal principal;
        try
        {
            principal = _tokenHandler.ValidateToken(token, validationParameters, out _);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DescopeAuthService] Token validation failed: {ex.Message}");
            _logger?.LogWarning(ex, "Descope token validation failed: {Message}", ex.Message);
            throw new UnauthorizedException("Invalid or expired Descope token.", ex);
        }

        var userId = principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("userId")?.Value
            ?? principal.FindFirst("uid")?.Value;

        if (string.IsNullOrWhiteSpace(userId))
        {
            Console.Error.WriteLine("[DescopeAuthService] Token does not contain a valid subject claim.");
            _logger?.LogWarning("Descope token missing subject (sub) claim.");
            throw new UnauthorizedException("Token does not contain a valid subject claim.");
        }

        var email = principal.FindFirst("email")?.Value
            ?? principal.FindFirst(ClaimTypes.Email)?.Value;

        var name = principal.FindFirst("name")?.Value
            ?? principal.FindFirst(ClaimTypes.Name)?.Value;

        return new DescopeUserData
        {
            UserId = userId,
            Email = email,
            Name = name
        };
    }

    private sealed class DescopeJwksConfigurationRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
    {
        private static readonly IConfigurationRetriever<OpenIdConnectConfiguration> DefaultRetriever = new OpenIdConnectConfigurationRetriever();

        public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
            string address,
            IDocumentRetriever retriever,
            CancellationToken cancel)
        {
            retriever ??= new HttpDocumentRetriever();
            var doc = await retriever.GetDocumentAsync(address, cancel).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(doc) && doc.Contains("\"keys\""))
            {
                var jwks = new JsonWebKeySet(doc);
                var config = new OpenIdConnectConfiguration
                {
                    JsonWebKeySet = jwks
                };

                foreach (var securityKey in jwks.GetSigningKeys())
                {
                    config.SigningKeys.Add(securityKey);
                }

                return config;
            }

            return await DefaultRetriever.GetConfigurationAsync(address, retriever, cancel).ConfigureAwait(false);
        }
    }
}
