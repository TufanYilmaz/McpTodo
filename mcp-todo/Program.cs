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

    // RFC 9728: OAuth 2.0 Protected Resource Metadata for Model Context Protocol
    app.MapGet("/.well-known/oauth-protected-resource", (IConfiguration config, HttpContext httpContext) =>
    {
        var serverUrl = config["SERVER_URL"]
            ?? Environment.GetEnvironmentVariable("SERVER_URL")
            ?? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var projectId = config["Descope:ProjectId"]
            ?? Environment.GetEnvironmentVariable("DESCOPE_PROJECT_ID")
            ?? "Peuc13MEWFEtDVhrNkOQ9eLNzk8Bh9sx";
        var appId = config["Descope:AppId"]
            ?? Environment.GetEnvironmentVariable("DESCOPE_APP_ID")
            ?? "RS3MEWRRsPXqQjyj5MOguKgo2sqTi";

        var host = projectId.StartsWith("Peuc1", StringComparison.OrdinalIgnoreCase)
            ? "https://api.euc1.descope.com"
            : "https://api.descope.com";

        var authServer = !string.IsNullOrWhiteSpace(appId)
            ? $"{host}/v1/apps/agentic/{projectId}/{appId}"
            : $"{host}/{projectId}";

        var authServers = new List<string> { authServer };
        if (host.Contains("euc1"))
        {
            authServers.Add($"https://api.descope.com/v1/apps/agentic/{projectId}/{appId}");
        }

        return Results.Json(new
        {
            authorization_servers = authServers,
            bearer_methods_supported = new[] { "header" },
            resource = $"{serverUrl.TrimEnd('/')}/mcp",
            resource_documentation = $"{serverUrl.TrimEnd('/')}/",
            scopes_supported = Array.Empty<string>()
        });
    });

    // OAuth 401 Challenge Middleware for unauthenticated MCP requests
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isMcpRequest = path.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase)
            || (path == "/" && HttpMethods.IsPost(context.Request.Method));

        if (isMcpRequest && !context.Request.Headers.ContainsKey("Authorization"))
        {
            var serverUrl = app.Configuration["SERVER_URL"]
                ?? Environment.GetEnvironmentVariable("SERVER_URL")
                ?? $"{context.Request.Scheme}://{context.Request.Host}";

            context.Response.Headers["WWW-Authenticate"] =
                $"Bearer realm=\"mcp\", resource_metadata=\"{serverUrl.TrimEnd('/')}/.well-known/oauth-protected-resource\"";
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

    var connectionString = configuration.GetConnectionString("Supabase")
        ?? configuration["ConnectionStrings:Supabase"]
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__Supabase")
        ?? Environment.GetEnvironmentVariable("SUPABASE_CONNECTION_STRING");

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
