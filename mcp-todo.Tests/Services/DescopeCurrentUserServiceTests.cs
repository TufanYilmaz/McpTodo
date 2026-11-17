using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using mcp_todo.Data;
using mcp_todo.Exceptions;
using mcp_todo.Models;
using mcp_todo.Services;
using Xunit;

namespace mcp_todo.Tests.Services;

public class DescopeCurrentUserServiceTests
{
    private readonly FakeDescopeAuthService _authService = new();
    private readonly FakeServiceScopeFactory _scopeFactory = new();

    [Fact]
    public void Implements_ICurrentUserService_Interface()
    {
        var config = new ConfigurationBuilder().Build();
        var httpAccessor = new HttpContextAccessor();
        var service = new DescopeCurrentUserService(_authService, config, httpAccessor, _scopeFactory);

        Assert.IsAssignableFrom<ICurrentUserService>(service);
    }

    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        var config = new ConfigurationBuilder().Build();
        var httpAccessor = new HttpContextAccessor();

        Assert.Throws<ArgumentNullException>("descopeAuthService", () =>
            new DescopeCurrentUserService(null!, config, httpAccessor, _scopeFactory));

        Assert.Throws<ArgumentNullException>("configuration", () =>
            new DescopeCurrentUserService(_authService, null!, httpAccessor, _scopeFactory));

        Assert.Throws<ArgumentNullException>("httpContextAccessor", () =>
            new DescopeCurrentUserService(_authService, config, null!, _scopeFactory));

        Assert.Throws<ArgumentNullException>("serviceScopeFactory", () =>
            new DescopeCurrentUserService(_authService, config, httpAccessor, null!));
    }

    [Fact]
    public void ResolveToken_FromHttpAuthorizationHeader_BearerToken_ReturnsToken()
    {
        var config = new ConfigurationBuilder().Build();
        var httpAccessor = new HttpContextAccessor();
        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] = "Bearer jwt-test-token-123";
        httpAccessor.HttpContext = context;

        var service = new DescopeCurrentUserService(_authService, config, httpAccessor, _scopeFactory);

        var token = service.ResolveToken();
        Assert.Equal("jwt-test-token-123", token);
    }

    [Fact]
    public void ResolveToken_FromHttpAuthorizationHeader_CaseInsensitiveBearer_ReturnsToken()
    {
        var config = new ConfigurationBuilder().Build();
        var httpAccessor = new HttpContextAccessor();
        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] = "bearer   case-insensitive-token  ";
        httpAccessor.HttpContext = context;

        var service = new DescopeCurrentUserService(_authService, config, httpAccessor, _scopeFactory);

        var token = service.ResolveToken();
        Assert.Equal("case-insensitive-token", token);
    }

    [Fact]
    public void ResolveToken_FromConfiguration_DESCOPE_USER_TOKEN_ReturnsToken()
    {
        var settings = new Dictionary<string, string?>
        {
            { "DESCOPE_USER_TOKEN", "config-token-xyz" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var httpAccessor = new HttpContextAccessor();

        var service = new DescopeCurrentUserService(_authService, config, httpAccessor, _scopeFactory);

        var token = service.ResolveToken();
        Assert.Equal("config-token-xyz", token);
    }

    [Fact]
    public void ResolveToken_FromConfiguration_DescopeUserToken_Colon_ReturnsToken()
    {
        var settings = new Dictionary<string, string?>
        {
            { "Descope:UserToken", "colon-token-abc" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var httpAccessor = new HttpContextAccessor();

        var service = new DescopeCurrentUserService(_authService, config, httpAccessor, _scopeFactory);

        var token = service.ResolveToken();
        Assert.Equal("colon-token-abc", token);
    }

    [Fact]
    public void ResolveToken_FromConfiguration_DescopeUserToken_DoubleUnderscore_ReturnsToken()
    {
        var settings = new Dictionary<string, string?>
        {
            { "Descope__UserToken", "dunder-token-def" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var httpAccessor = new HttpContextAccessor();

        var service = new DescopeCurrentUserService(_authService, config, httpAccessor, _scopeFactory);

        var token = service.ResolveToken();
        Assert.Equal("dunder-token-def", token);
    }

    [Fact]
    public void ResolveToken_WhenBearerPrefixInConfiguration_StripsBearerPrefix()
    {
        var settings = new Dictionary<string, string?>
        {
            { "DESCOPE_USER_TOKEN", "Bearer config-bearer-token" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var httpAccessor = new HttpContextAccessor();

        var service = new DescopeCurrentUserService(_authService, config, httpAccessor, _scopeFactory);

        var token = service.ResolveToken();
        Assert.Equal("config-bearer-token", token);
    }

    [Fact]
    public void ResolveToken_WhenNoTokenPresent_ReturnsNull()
    {
        var config = new ConfigurationBuilder().Build();
        var httpAccessor = new HttpContextAccessor();

        var service = new DescopeCurrentUserService(_authService, config, httpAccessor, _scopeFactory);

        var token = service.ResolveToken();
        Assert.Null(token);
    }

    [Fact]
    public void UserId_WhenNoTokenPresent_ThrowsUnauthorizedException()
    {
        var config = new ConfigurationBuilder().Build();
        var httpAccessor = new HttpContextAccessor();
        var service = new DescopeCurrentUserService(_authService, config, httpAccessor, _scopeFactory);

        var ex = Assert.Throws<UnauthorizedException>(() => _ = service.UserId);
        Assert.Contains("Descope user token is missing", ex.Message);
    }

    [Fact]
    public void GetRequiredUserId_WhenNoTokenPresent_ThrowsUnauthorizedException()
    {
        var config = new ConfigurationBuilder().Build();
        var httpAccessor = new HttpContextAccessor();
        var service = new DescopeCurrentUserService(_authService, config, httpAccessor, _scopeFactory);

        var ex = Assert.Throws<UnauthorizedException>(() => service.GetRequiredUserId());
        Assert.Contains("Descope user token is missing", ex.Message);
    }

    [Fact]
    public void UserId_WhenDescopeAuthServiceThrowsUnauthorizedException_PropagatesUnauthorizedException()
    {
        _authService.ExceptionToThrow = new UnauthorizedException("Invalid or expired Descope token.");

        var settings = new Dictionary<string, string?> { { "DESCOPE_USER_TOKEN", "invalid-token" } };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var httpAccessor = new HttpContextAccessor();
        var service = new DescopeCurrentUserService(_authService, config, httpAccessor, _scopeFactory);

        var ex = Assert.Throws<UnauthorizedException>(() => _ = service.UserId);
        Assert.Equal("Invalid or expired Descope token.", ex.Message);
    }

    [Fact]
    public void GetRequiredUserId_WhenDescopeAuthServiceThrowsUnexpectedException_WrapsInUnauthorizedException()
    {
        _authService.ExceptionToThrow = new InvalidOperationException("External service unavailable");

        var settings = new Dictionary<string, string?> { { "DESCOPE_USER_TOKEN", "any-token" } };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var httpAccessor = new HttpContextAccessor();
        var service = new DescopeCurrentUserService(_authService, config, httpAccessor, _scopeFactory);

        var ex = Assert.Throws<UnauthorizedException>(() => service.GetRequiredUserId());
        Assert.Equal("Unauthorized: Descope token validation failed.", ex.Message);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact]
    public void UserId_WhenTokenValid_ReturnsUserId_AndCachesResult()
    {
        _authService.UserDataToReturn = new DescopeUserData
        {
            UserId = "descope-user-789",
            Email = "user789@example.com",
            Name = "John Doe"
        };

        var settings = new Dictionary<string, string?> { { "DESCOPE_USER_TOKEN", "valid-token-789" } };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var httpAccessor = new HttpContextAccessor();
        var service = new DescopeCurrentUserService(_authService, config, httpAccessor, _scopeFactory);

        // First call
        var userId1 = service.UserId;
        Assert.Equal("descope-user-789", userId1);
        Assert.Equal(1, _authService.ValidateCallCount);

        // Second call should use cache
        var userId2 = service.UserId;
        Assert.Equal("descope-user-789", userId2);
        Assert.Equal(1, _authService.ValidateCallCount);

        // GetRequiredUserId should also use cached user
        var requiredUserId = service.GetRequiredUserId();
        Assert.Equal("descope-user-789", requiredUserId);
        Assert.Equal(1, _authService.ValidateCallCount);
    }

    [Fact]
    public void EnsureUserProvisioned_CreatesScopeViaServiceScopeFactory()
    {
        var config = new ConfigurationBuilder().Build();
        var httpAccessor = new HttpContextAccessor();
        var service = new DescopeCurrentUserService(_authService, config, httpAccessor, _scopeFactory);

        var userData = new DescopeUserData
        {
            UserId = "user-to-provision",
            Email = "provision@example.com",
            Name = "Provision User"
        };

        service.EnsureUserProvisioned(userData);

        Assert.True(_scopeFactory.ScopeCreated);
    }

    [Fact]
    public void EnsureUserProvisioned_WhenUserDataIsNull_DoesNotCreateScope()
    {
        var config = new ConfigurationBuilder().Build();
        var httpAccessor = new HttpContextAccessor();
        var service = new DescopeCurrentUserService(_authService, config, httpAccessor, _scopeFactory);

        service.EnsureUserProvisioned(null!);

        Assert.False(_scopeFactory.ScopeCreated);
    }

    private sealed class FakeDescopeAuthService : IDescopeAuthService
    {
        public DescopeUserData? UserDataToReturn { get; set; }
        public Exception? ExceptionToThrow { get; set; }
        public int ValidateCallCount { get; private set; }

        public Task<DescopeUserData> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
        {
            ValidateCallCount++;

            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(UserDataToReturn ?? new DescopeUserData
            {
                UserId = "default-mock-user",
                Email = "mock@descope.com",
                Name = "Mock User"
            });
        }
    }

    private sealed class FakeServiceScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        public bool ScopeCreated { get; private set; }

        public IServiceScope CreateScope()
        {
            ScopeCreated = true;
            return this;
        }

        public IServiceProvider ServiceProvider => this;

        public object? GetService(Type serviceType)
        {
            return null;
        }

        public void Dispose()
        {
        }
    }
}
