using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using mcp_todo.Data;
using mcp_todo.Exceptions;
using mcp_todo.Models;

namespace mcp_todo.Services;

public class DescopeCurrentUserService : ICurrentUserService
{
    private static readonly AsyncLocal<bool> IsProvisioning = new();

    private readonly IDescopeAuthService _descopeAuthService;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    private readonly object _lock = new();
    private DescopeUserData? _cachedUserData;
    private bool _isInitialized;
    private Exception? _initializationException;

    public DescopeCurrentUserService(
        IDescopeAuthService descopeAuthService,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        IServiceScopeFactory serviceScopeFactory)
    {
        _descopeAuthService = descopeAuthService ?? throw new ArgumentNullException(nameof(descopeAuthService));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
    }

    public string? UserId => GetOrValidateUser().UserId;

    public string GetRequiredUserId()
    {
        var user = GetOrValidateUser();
        if (string.IsNullOrWhiteSpace(user.UserId))
        {
            throw new UnauthorizedException("Unauthorized: Valid user identifier could not be extracted from the token.");
        }

        return user.UserId;
    }

    public string? ResolveToken()
    {
        // 1. Check HTTP Authorization header (Bearer <token>)
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null && httpContext.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var headerStr = authHeader.ToString().Trim();
            if (headerStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = headerStr.Substring("Bearer ".Length).Trim();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token;
                }
            }
        }

        // 2. Check configuration and environment variables (Stdio mode)
        var envToken = _configuration["DESCOPE_USER_TOKEN"]
            ?? _configuration["Descope:UserToken"]
            ?? _configuration["Descope__UserToken"]
            ?? Environment.GetEnvironmentVariable("DESCOPE_USER_TOKEN");

        if (!string.IsNullOrWhiteSpace(envToken))
        {
            var trimmedToken = envToken.Trim();
            if (trimmedToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                trimmedToken = trimmedToken.Substring("Bearer ".Length).Trim();
            }

            if (!string.IsNullOrWhiteSpace(trimmedToken))
            {
                return trimmedToken;
            }
        }

        return null;
    }

    public void EnsureUserProvisioned(DescopeUserData userData)
    {
        if (userData == null || string.IsNullOrWhiteSpace(userData.UserId))
        {
            return;
        }

        if (IsProvisioning.Value)
        {
            return;
        }

        try
        {
            IsProvisioning.Value = true;
            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetService<AppDbContext>();
            if (context == null)
            {
                return;
            }

            var exists = context.Users.Any(u => u.Id == userData.UserId);
            if (!exists)
            {
                var email = !string.IsNullOrWhiteSpace(userData.Email)
                    ? userData.Email
                    : $"{userData.UserId}@descope.user";

                var name = !string.IsNullOrWhiteSpace(userData.Name)
                    ? userData.Name
                    : "Descope User";

                var newUser = new User
                {
                    Id = userData.UserId,
                    Email = email,
                    Name = name,
                    CreatedAt = DateTime.UtcNow
                };

                context.Users.Add(newUser);
                try
                {
                    context.SaveChanges();
                }
                catch (DbUpdateException)
                {
                    // Concurrency conflict / race condition: another thread/request already created this user.
                    // Safe to ignore per D-26.
                }
            }
        }
        catch (Exception ex) when (ex is not UnauthorizedException)
        {
            Console.Error.WriteLine($"[DescopeCurrentUserService] User provisioning warning: {ex.Message}");
        }
        finally
        {
            IsProvisioning.Value = false;
        }
    }

    private DescopeUserData GetOrValidateUser()
    {
        if (_isInitialized)
        {
            if (_initializationException != null)
            {
                throw _initializationException;
            }
            return _cachedUserData!;
        }

        lock (_lock)
        {
            if (_isInitialized)
            {
                if (_initializationException != null)
                {
                    throw _initializationException;
                }
                return _cachedUserData!;
            }

            try
            {
                var token = ResolveToken();
                if (string.IsNullOrWhiteSpace(token))
                {
                    throw new UnauthorizedException("Unauthorized: Descope user token is missing. Please provide Authorization header or DESCOPE_USER_TOKEN environment variable.");
                }

                _cachedUserData = _descopeAuthService.ValidateTokenAsync(token).GetAwaiter().GetResult();
                if (_cachedUserData == null || string.IsNullOrWhiteSpace(_cachedUserData.UserId))
                {
                    throw new UnauthorizedException("Unauthorized: Valid user identifier could not be extracted from the token.");
                }

                EnsureUserProvisioned(_cachedUserData);
                _isInitialized = true;
                return _cachedUserData;
            }
            catch (Exception ex)
            {
                _initializationException = ex is UnauthorizedException
                    ? ex
                    : new UnauthorizedException("Unauthorized: Descope token validation failed.", ex);
                _isInitialized = true;
                throw _initializationException;
            }
        }
    }
}
