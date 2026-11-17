using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using mcp_todo.Data;
using mcp_todo.Services;
using mcp_todo.Tools;

var builder = Host.CreateApplicationBuilder(args);

// D-12: Redirect all console logging to stderr so stdout is strictly reserved for JSON-RPC messages.
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Load configuration
builder.Configuration.AddUserSecrets<AppDbContextFactory>(optional: true);

var connectionString = builder.Configuration.GetConnectionString("Supabase")
    ?? builder.Configuration["ConnectionStrings:Supabase"]
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__Supabase")
    ?? Environment.GetEnvironmentVariable("SUPABASE_CONNECTION_STRING");

// Dependency Injection
builder.Services.AddScoped<ICurrentUserService, DevCurrentUserService>();
builder.Services.AddScoped<ProjectTools>();
builder.Services.AddScoped<TodoTools>();

builder.Services.AddDbContext<AppDbContext>(options =>
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

// MCP Server registration
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

if (args.Contains("--verify-tools"))
{
    Console.Error.WriteLine("=== MCP TOOLS VERIFICATION ===");
    var toolTypes = new[] { typeof(ProjectTools), typeof(TodoTools) };
    var toolCount = 0;
    foreach (var type in toolTypes)
    {
        Console.Error.WriteLine($"[ToolType] {type.Name}");
        var methods = type.GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), false).Any());
        foreach (var m in methods)
        {
            toolCount++;
            var toolAttr = (ModelContextProtocol.Server.McpServerToolAttribute)m.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), false).First();
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

    return;
}

var host = builder.Build();
await host.RunAsync();
