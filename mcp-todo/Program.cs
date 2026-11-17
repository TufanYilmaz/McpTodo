using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using mcp_todo.Data;
using mcp_todo.Services;
using mcp_todo.Tools;

// Check mode: Stdio (CLI desktop) or Web / HTTP (Render / Cloud / Webhook)
var isPortDefined = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PORT"));
var isHttpArg = args.Contains("--http");
var isStdioArg = args.Contains("--stdio");
var isWebMode = isPortDefined || isHttpArg || (!isStdioArg && !Console.IsInputRedirected);

if (!isWebMode)
{
    // ==========================================
    // 1. STDIO MODE (Claude Desktop / Cursor CLI)
    // ==========================================
    var stdioBuilder = Host.CreateApplicationBuilder(args);

    // D-12: Redirect all console logging to stderr so stdout is strictly reserved for JSON-RPC messages.
    stdioBuilder.Logging.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });

    ConfigureServices(stdioBuilder.Configuration, stdioBuilder.Services);

    stdioBuilder.Services.AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    if (args.Contains("--verify-tools"))
    {
        RunToolsVerification();
        return;
    }

    var stdioHost = stdioBuilder.Build();
    await stdioHost.RunAsync();
}
else
{
    // ==========================================
    // 2. WEB / HTTP & SSE MODE (Render.com / Cloud)
    // ==========================================
    var webBuilder = WebApplication.CreateBuilder(args);

    // Support Render.com dynamic PORT
    var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
    webBuilder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(int.Parse(port));
    });

    ConfigureServices(webBuilder.Configuration, webBuilder.Services);

    // Register MCP Server with modern Streamable HTTP transport (/mcp)
    webBuilder.Services.AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    var app = webBuilder.Build();

    static string GetPublicServerUrl(IConfiguration config, HttpContext httpContext)
    {
        var url = config["SERVER_URL"]
            ?? Environment.GetEnvironmentVariable("SERVER_URL")
            ?? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.Contains("localhost"))
        {
            url = "https://" + url.Substring("http://".Length);
        }
        return url.TrimEnd('/');
    }

    static string GetDescopeProjectId(IConfiguration config)
    {
        return config["Descope:ProjectId"]
            ?? config["Descope__ProjectId"]
            ?? Environment.GetEnvironmentVariable("DESCOPE_PROJECT_ID")
            ?? string.Empty;
    }

    static string GetDescopeAppId(IConfiguration config)
    {
        return config["Descope:AppId"]
            ?? config["Descope__AppId"]
            ?? Environment.GetEnvironmentVariable("DESCOPE_APP_ID")
            ?? string.Empty;
    }

    // RFC 9728: OAuth 2.0 Protected Resource Metadata for Model Context Protocol
    var protectedResourceHandler = (IConfiguration config, HttpContext httpContext) =>
    {
        var serverUrl = GetPublicServerUrl(config, httpContext);
        var projectId = GetDescopeProjectId(config);
        var appId = GetDescopeAppId(config);

        if (string.IsNullOrWhiteSpace(projectId))
        {
            return Results.Problem("DESCOPE_PROJECT_ID environment variable is not configured.", statusCode: StatusCodes.Status500InternalServerError);
        }

        var host = projectId.StartsWith("Peuc1", StringComparison.OrdinalIgnoreCase)
            ? "https://api.euc1.descope.com"
            : "https://api.descope.com";

        var authServer = !string.IsNullOrWhiteSpace(appId)
            ? $"{host}/v1/apps/agentic/{projectId}/{appId}"
            : $"{host}/{projectId}";

        var authServers = new List<string> { serverUrl, authServer };
        if (host.Contains("euc1"))
        {
            authServers.Add($"https://api.descope.com/v1/apps/agentic/{projectId}/{appId}");
        }

        return Results.Json(new
        {
            authorization_servers = authServers,
            bearer_methods_supported = new[] { "header" },
            resource = $"{serverUrl}/mcp",
            resource_documentation = $"{serverUrl}/",
            scopes_supported = Array.Empty<string>()
        });
    };

    app.MapGet("/.well-known/oauth-protected-resource", protectedResourceHandler);
    app.MapGet("/.well-known/oauth-protected-resource/mcp", protectedResourceHandler);

    // RFC 8414: OAuth 2.0 Authorization Server Metadata (Fallback discovery for MCP clients)
    app.MapGet("/.well-known/oauth-authorization-server", (IConfiguration config, HttpContext httpContext) =>
    {
        var serverUrl = GetPublicServerUrl(config, httpContext);
        var projectId = GetDescopeProjectId(config);
        var appId = GetDescopeAppId(config);

        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(appId))
        {
            return Results.Problem("DESCOPE_PROJECT_ID or DESCOPE_APP_ID is not configured.", statusCode: StatusCodes.Status500InternalServerError);
        }

        var host = projectId.StartsWith("Peuc1", StringComparison.OrdinalIgnoreCase)
            ? "https://api.euc1.descope.com"
            : "https://api.descope.com";

        return Results.Json(new
        {
            issuer = serverUrl,
            authorization_endpoint = $"{host}/oauth2/v1/apps/agentic/{projectId}/{appId}/authorize",
            token_endpoint = $"{host}/oauth2/v1/apps/agentic/{projectId}/{appId}/token",
            jwks_uri = $"{host}/{projectId}/.well-known/jwks.json",
            registration_endpoint = $"{serverUrl}/register",
            response_types_supported = new[] { "code" },
            grant_types_supported = new[] { "authorization_code", "refresh_token" },
            token_endpoint_auth_methods_supported = new[] { "none", "client_secret_post" },
            code_challenge_methods_supported = new[] { "S256" }
        });
    });

    // RFC 7591: OAuth 2.0 Dynamic Client Registration Proxy to Descope
    app.MapPost("/register", async (HttpContext httpContext, IConfiguration config) =>
    {
        var projectId = GetDescopeProjectId(config);
        var appId = GetDescopeAppId(config);

        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(appId))
        {
            return Results.Problem("DESCOPE_PROJECT_ID or DESCOPE_APP_ID is not configured.", statusCode: StatusCodes.Status500InternalServerError);
        }

        var host = projectId.StartsWith("Peuc1", StringComparison.OrdinalIgnoreCase)
            ? "https://api.euc1.descope.com"
            : "https://api.descope.com";

        var descopeRegisterUrl = $"{host}/v1/mgmt/mcp/client/{projectId}/{appId}/register";

        using var reader = new System.IO.StreamReader(httpContext.Request.Body);
        var requestBody = await reader.ReadToEndAsync();

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; McpTodoProxy/1.0)");

            using var content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
            var response = await client.PostAsync(descopeRegisterUrl, content);

            var responseBody = await response.Content.ReadAsStringAsync();
            return Results.Content(responseBody, "application/json", statusCode: (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[RegisterProxy] Error forwarding to Descope: {ex.Message}");
            return Results.Problem("Failed to register client with Descope.", statusCode: StatusCodes.Status502BadGateway);
        }
    });

    // OAuth 401 Challenge Middleware for unauthenticated MCP requests
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isMcpRequest = path.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase)
            || (path == "/" && HttpMethods.IsPost(context.Request.Method));

        if (isMcpRequest && !context.Request.Headers.ContainsKey("Authorization"))
        {
            var serverUrl = GetPublicServerUrl(app.Configuration, context);

            context.Response.Headers["WWW-Authenticate"] =
                $"Bearer realm=\"mcp\", resource_metadata=\"{serverUrl}/.well-known/oauth-protected-resource/mcp\"";
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "unauthorized",
                message = "Authentication required. Please authenticate via OAuth."
            });
            return;
        }

        await next();
    });

    // Map MCP Endpoint at /mcp (e.g. site.com/mcp)
    app.MapMcp("/mcp");

    // Also map root MCP endpoint for clients expecting /
    app.MapMcp();

    // Health Check Endpoint (For Render.com and monitoring)
    app.MapGet("/health", () => Results.Ok(new
    {
        status = "healthy",
        service = "McpServer.Todo",
        timestamp = DateTime.UtcNow
    }));

    // Informational Root Endpoint (Friendly browser response)
    app.MapGet("/", () => Results.Ok(new
    {
        service = "McpServer.Todo",
        description = "Model Context Protocol Server for Supabase & Descope",
        mcpEndpoint = "/mcp",
        healthEndpoint = "/health",
        availableTools = new[]
        {
            "project_list", "project_create", "project_delete",
            "todo_list", "todo_add", "todo_complete", "todo_delete"
        }
    }));

    if (args.Contains("--verify-tools"))
    {
        RunToolsVerification();
        return;
    }

    Console.WriteLine($"[INFO] MCP Server started in HTTP/SSE Web Mode on port {port}.");
    Console.WriteLine($"[INFO] MCP Endpoint: http://localhost:{port}/mcp");
    Console.WriteLine($"[INFO] Health Check: http://localhost:{port}/health");

    await app.RunAsync();
}

static void ConfigureServices(IConfigurationManager configuration, IServiceCollection services)
{
    configuration.AddUserSecrets<AppDbContextFactory>(optional: true);

    var rawConnectionString = configuration.GetConnectionString("Supabase")
        ?? configuration["ConnectionStrings:Supabase"]
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__Supabase")
        ?? Environment.GetEnvironmentVariable("SUPABASE_CONNECTION_STRING")
        ?? Environment.GetEnvironmentVariable("DATABASE_URL");

    var connectionString = NormalizeConnectionString(rawConnectionString);

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine("[WARNING] No Supabase/Database connection string found! Please set SUPABASE_CONNECTION_STRING or DATABASE_URL.");
    }

    services.AddHttpContextAccessor();
    services.AddSingleton<IDescopeAuthService, DescopeAuthService>();
    services.AddScoped<ICurrentUserService, DescopeCurrentUserService>();
    services.AddScoped<ProjectTools>();
    services.AddScoped<TodoTools>();

    services.AddDbContext<AppDbContext>(options =>
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null);
            });
        }
    });
}

static string? NormalizeConnectionString(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return raw;
    raw = raw.Trim();

    // If it's already a standard key-value connection string (contains "Host=" or "Server=")
    if (raw.Contains("Host=", StringComparison.OrdinalIgnoreCase) || raw.Contains("Server=", StringComparison.OrdinalIgnoreCase))
    {
        return raw;
    }

    if (raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            var uri = new Uri(raw);
            var userInfo = uri.UserInfo.Split(':');
            var user = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "postgres";
            var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var host = uri.Host;
            var port = uri.Port > 0 ? uri.Port : 5432;
            var db = uri.AbsolutePath.TrimStart('/');
            if (string.IsNullOrWhiteSpace(db)) db = "postgres";

            var sb = new System.Text.StringBuilder();
            sb.Append($"Host={host};Port={port};Database={db};Username={user};");
            if (!string.IsNullOrEmpty(pass))
            {
                sb.Append($"Password={pass};");
            }
            sb.Append("SSL Mode=Require;Trust Server Certificate=true;");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARNING] Failed to parse URI connection string: {ex.Message}");
        }
    }

    return raw;
}

static void RunToolsVerification()
{
    Console.Error.WriteLine("=== MCP TOOLS VERIFICATION ===");
    var toolTypes = new[] { typeof(ProjectTools), typeof(TodoTools) };
    var toolCount = 0;
    foreach (var type in toolTypes)
    {
        Console.Error.WriteLine($"[ToolType] {type.Name}");
        var methods = type.GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false).Any());
        foreach (var m in methods)
        {
            toolCount++;
            var toolAttr = (McpServerToolAttribute)m.GetCustomAttributes(typeof(McpServerToolAttribute), false).First();
            var descAttr = (System.ComponentModel.DescriptionAttribute?)m.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).FirstOrDefault();
            Console.Error.WriteLine($"  -> Tool: {toolAttr.Name} | Title: {toolAttr.Title} | Desc: {descAttr?.Description}");
            foreach (var p in m.GetParameters())
            {
                if (p.ParameterType != typeof(CancellationToken))
                {
                    var pDesc = (System.ComponentModel.DescriptionAttribute?)p.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).FirstOrDefault();
                    Console.Error.WriteLine($"       Param: {p.Name} ({p.ParameterType.Name}, Optional={p.IsOptional}) | Desc: {pDesc?.Description}");
                }
            }
        }
    }
    Console.Error.WriteLine($"=== TOTAL TOOLS: {toolCount} ===");

    // Test MarkdownFormatter
    var testProject = new mcp_todo.Models.Project { Id = 1, Name = "İş Projesi" };
    var testTodos = new List<mcp_todo.Models.Todo>
    {
        new() { Id = 101, Title = "Rapor Hazırla", DueDate = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), IsCompleted = false },
        new() { Id = 102, Title = "Mail Gönder", DueDate = null, IsCompleted = true }
    };
    var singleOutput = mcp_todo.Tools.Formatters.MarkdownFormatter.FormatTodoList(testTodos);
    Console.Error.WriteLine("--- FormatTodoList Test Output ---");
    Console.Error.WriteLine(singleOutput);

    var grouping = new mcp_todo.Tools.Formatters.ProjectTodoGrouping(testProject, testTodos);
    var groupedOutput = mcp_todo.Tools.Formatters.MarkdownFormatter.FormatGroupedTodoList(new[] { grouping });
    Console.Error.WriteLine("--- FormatGroupedTodoList Test Output ---");
    Console.Error.WriteLine(groupedOutput);
}
