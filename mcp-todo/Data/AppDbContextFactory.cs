using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using mcp_todo.Services;

namespace mcp_todo.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<AppDbContextFactory>(optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("Supabase")
            ?? configuration["ConnectionStrings:Supabase"]
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Supabase")
            ?? Environment.GetEnvironmentVariable("SUPABASE_CONNECTION_STRING")
            ?? "Host=localhost;Database=mcp_todo;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorCodesToAdd: null);
        });

        return new AppDbContext(optionsBuilder.Options, new DevCurrentUserService());
    }
}
