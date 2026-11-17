using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using mcp_todo.Exceptions;
using mcp_todo.Services;
using Xunit;

namespace mcp_todo.Tests.Services;

public class DescopeAuthServiceTests
{
    private const string ProjectId = "test-project-id";

    [Fact]
    public void Constructor_WithoutProjectId_ThrowsInvalidOperationException()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => new DescopeAuthService(configuration));
        Assert.Equal("Descope:ProjectId is not configured.", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task ValidateTokenAsync_WithNullOrEmptyToken_ThrowsUnauthorizedException(string? token)
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Descope:ProjectId", ProjectId }
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        var service = new DescopeAuthService(configuration);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => service.ValidateTokenAsync(token!));
        Assert.Equal("Token is missing or empty.", ex.Message);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithInvalidTokenFormat_ThrowsUnauthorizedException()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Descope:ProjectId", ProjectId }
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        using var rsa = RSA.Create(2048);
        var rsaKey = new RsaSecurityKey(rsa) { KeyId = "test-key-id" };
        var config = new OpenIdConnectConfiguration();
        config.SigningKeys.Add(rsaKey);

        var configManager = new TestConfigurationManager(config);
        var service = new DescopeAuthService(configuration, logger: null, configurationManager: configManager);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => service.ValidateTokenAsync("not-a-valid-jwt"));
        Assert.Equal("Invalid or expired Descope token.", ex.Message);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithValidToken_ReturnsDescopeUserData()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Descope:ProjectId", ProjectId }
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        using var rsa = RSA.Create(2048);
        var rsaKey = new RsaSecurityKey(rsa) { KeyId = "test-key-id" };
        var config = new OpenIdConnectConfiguration();
        config.SigningKeys.Add(rsaKey);

        var configManager = new TestConfigurationManager(config);
        var service = new DescopeAuthService(configuration, logger: null, configurationManager: configManager);

        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = $"https://api.descope.com/{ProjectId}",
            Audience = ProjectId,
            Expires = DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256),
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", "descope-user-123"),
                new Claim("email", "test@example.com"),
                new Claim("name", "Test User")
            })
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var jwtString = tokenHandler.WriteToken(token);

        // Act
        var result = await service.ValidateTokenAsync(jwtString);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("descope-user-123", result.UserId);
        Assert.Equal("test@example.com", result.Email);
        Assert.Equal("Test User", result.Name);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithExpiredToken_ThrowsUnauthorizedException()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Descope:ProjectId", ProjectId }
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        using var rsa = RSA.Create(2048);
        var rsaKey = new RsaSecurityKey(rsa) { KeyId = "test-key-id" };
        var config = new OpenIdConnectConfiguration();
        config.SigningKeys.Add(rsaKey);

        var configManager = new TestConfigurationManager(config);
        var service = new DescopeAuthService(configuration, logger: null, configurationManager: configManager);

        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = $"https://api.descope.com/{ProjectId}",
            Audience = ProjectId,
            NotBefore = DateTime.UtcNow.AddHours(-2),
            Expires = DateTime.UtcNow.AddHours(-1), // Expired
            SigningCredentials = new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256),
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", "user-123")
            })
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var jwtString = tokenHandler.WriteToken(token);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => service.ValidateTokenAsync(jwtString));
        Assert.Equal("Invalid or expired Descope token.", ex.Message);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithoutSubjectClaim_ThrowsUnauthorizedException()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Descope:ProjectId", ProjectId }
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        using var rsa = RSA.Create(2048);
        var rsaKey = new RsaSecurityKey(rsa) { KeyId = "test-key-id" };
        var config = new OpenIdConnectConfiguration();
        config.SigningKeys.Add(rsaKey);

        var configManager = new TestConfigurationManager(config);
        var service = new DescopeAuthService(configuration, logger: null, configurationManager: configManager);

        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = $"https://api.descope.com/{ProjectId}",
            Audience = ProjectId,
            Expires = DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256),
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("email", "no-sub@example.com")
            })
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var jwtString = tokenHandler.WriteToken(token);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => service.ValidateTokenAsync(jwtString));
        Assert.Equal("Token does not contain a valid subject claim.", ex.Message);
    }

    private sealed class TestConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
    {
        private readonly OpenIdConnectConfiguration _configuration;

        public TestConfigurationManager(OpenIdConnectConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            return Task.FromResult(_configuration);
        }

        public void RequestRefresh()
        {
        }
    }
}
