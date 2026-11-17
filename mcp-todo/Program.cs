using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using mcp_todo.Data;
using mcp_todo.Models;
using mcp_todo.Services;

Console.WriteLine("=== MCP Todo EF Core Verification ===");

// 1. Build Configuration
var configuration = new ConfigurationBuilder()
    .AddUserSecrets<AppDbContextFactory>(optional: true)
    .Build();

var connectionString = configuration.GetConnectionString("Supabase")
    ?? configuration["ConnectionStrings:Supabase"]
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__Supabase")
    ?? Environment.GetEnvironmentVariable("SUPABASE_CONNECTION_STRING");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("[ERROR] Supabase connection Env değerlerinden oku");
    return;
}

Console.WriteLine("[INFO] Başarılı bir şekilde yüklendi.");

// 2. Setup Dependency Injection
var services = new ServiceCollection();
services.AddScoped<ICurrentUserService, DevCurrentUserService>();
services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
    });
});

var serviceProvider = services.BuildServiceProvider();

using var scope = serviceProvider.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
var currentUserService = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();

// 3. Test Query String generation (Global Query Filter verification)
Console.WriteLine($"[INFO] Current User ID: {currentUserService.UserId}");
var projectSql = db.Projects.ToQueryString();
Console.WriteLine($"[DEBUG] Project SQL: {projectSql}");

bool filterVerified = projectSql.Contains("user_id", StringComparison.OrdinalIgnoreCase) &&
                      projectSql.Contains("is_deleted", StringComparison.OrdinalIgnoreCase);

if (!filterVerified)
{
    Console.WriteLine("[ERROR] Query filter is not present in generated SQL!");
    return;
}
Console.WriteLine("[INFO] Global Query Filter SQL translation verified.");

// 4. Test Database Connection
Console.WriteLine("[INFO] Testing Supabase database connection...");
try
{
    await db.Database.OpenConnectionAsync();
    Console.WriteLine("[INFO] Connection opened successfully via OpenConnectionAsync.");
    await db.Database.CloseConnectionAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"[DEBUG] Connection exception: {ex.GetType().FullName}: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"[DEBUG] Inner exception: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
    }
}
bool canConnect = await db.Database.CanConnectAsync();
if (!canConnect)
{
    Console.WriteLine("[ERROR] Could not connect to Supabase database.");
    return;
}
Console.WriteLine("[INFO] Successfully connected to Supabase PostgreSQL database.");

// Apply migrations if needed to ensure tables exist
try
{
    Console.WriteLine("[INFO] Checking database migrations...");
    await db.Database.MigrateAsync();
    Console.WriteLine("[INFO] Migrations up to date.");
}
catch (Exception ex)
{
    Console.WriteLine($"[WARN] Migration check note: {ex.Message}");
}

// 5. Test CRUD, HasQueryFilter and Soft Delete in Database
var testUserId = currentUserService.GetRequiredUserId();

// Ensure test user exists
var user = await db.Users.FindAsync(testUserId);
if (user == null)
{
    user = new User
    {
        Id = testUserId,
        Email = "dev@example.com",
        Name = "Default Dev User"
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();
    Console.WriteLine($"[INFO] Created user: {testUserId}");
}

// Create test project
var project = new Project
{
    UserId = testUserId,
    Name = "Verification Project " + Guid.NewGuid().ToString("N")[..8],
    Description = "Testing query filter and soft delete"
};
db.Projects.Add(project);
await db.SaveChangesAsync();
Console.WriteLine($"[INFO] Created project with Id: {project.Id}");

// Create test todo
var todo = new Todo
{
    ProjectId = project.Id,
    UserId = testUserId,
    Title = "Verification Todo Item",
    DueDate = DateTime.UtcNow.AddDays(1)
};
db.Todos.Add(todo);
await db.SaveChangesAsync();
Console.WriteLine($"[INFO] Created todo with Id: {todo.Id}");

// Verify listing via Query Filter
var activeProjects = await db.Projects.ToListAsync();
if (!activeProjects.Any(p => p.Id == project.Id))
{
    Console.WriteLine("[ERROR] Created project not found via Query Filter!");
    return;
}

// Test Soft Delete on Todo
db.Todos.Remove(todo);
await db.SaveChangesAsync();
Console.WriteLine($"[INFO] Executed db.Todos.Remove(todo). IsDeleted in memory: {todo.IsDeleted}");

if (!todo.IsDeleted)
{
    Console.WriteLine("[ERROR] Todo.IsDeleted was not set to true after Remove()!");
    return;
}

// Verify Todo is filtered out by HasQueryFilter
var queryTodo = await db.Todos.FirstOrDefaultAsync(t => t.Id == todo.Id);
if (queryTodo != null)
{
    Console.WriteLine("[ERROR] Deleted todo was returned by standard query filter!");
    return;
}
Console.WriteLine("[INFO] Deleted todo is correctly filtered out by HasQueryFilter.");

// Verify Todo still exists in database when query filter is ignored
var rawTodo = await db.Todos.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == todo.Id);
if (rawTodo == null || !rawTodo.IsDeleted)
{
    Console.WriteLine("[ERROR] Todo record not found or not marked deleted when ignoring query filters!");
    return;
}
Console.WriteLine("[INFO] Todo verified physically present in DB with is_deleted = true.");

// Clean up: delete project (which soft-deletes project)
db.Projects.Remove(project);
await db.SaveChangesAsync();
Console.WriteLine($"[INFO] Executed db.Projects.Remove(project). IsDeleted in memory: {project.IsDeleted}");

var queryProject = await db.Projects.FirstOrDefaultAsync(p => p.Id == project.Id);
if (queryProject != null)
{
    Console.WriteLine("[ERROR] Deleted project was returned by standard query filter!");
    return;
}
Console.WriteLine("[INFO] Deleted project is correctly filtered out by HasQueryFilter.");

Console.WriteLine("[SUCCESS] EF Core AppDbContext and Query Filter verified.");
