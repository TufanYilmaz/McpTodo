using Microsoft.EntityFrameworkCore;
using Moq;
using mcp_todo.Data;
using mcp_todo.Models;
using mcp_todo.Services;
using Xunit;

namespace mcp_todo.Tests.Data;

public class AppDbContextTests
{
    private static (AppDbContext Context, Mock<ICurrentUserService> UserMock) CreateContext(
        string databaseName,
        string? currentUserId = "tenant-1")
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: databaseName)
            .Options;

        var userMock = new Mock<ICurrentUserService>();
        userMock.Setup(u => u.UserId).Returns(currentUserId);
        userMock.Setup(u => u.GetRequiredUserId()).Returns(currentUserId ?? string.Empty);

        var context = new AppDbContext(options, userMock.Object);
        return (context, userMock);
    }

    [Fact]
    public void Constructor_WithNullCurrentUserService_ThrowsArgumentNullException()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new AppDbContext(options, null!));
        Assert.Equal("currentUserService", exception.ParamName);
    }

    [Fact]
    public async Task GlobalQueryFilter_FiltersProjectsAndTodosByCurrentUserId()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();

        // 1. Seed data using context 1 (tenant-1)
        await using (var seedContext = CreateContext(dbName, "tenant-1").Context)
        {
            var user1 = new User { Id = "tenant-1", Email = "user1@example.com", Name = "User 1" };
            var user2 = new User { Id = "tenant-2", Email = "user2@example.com", Name = "User 2" };
            seedContext.Users.AddRange(user1, user2);

            var project1 = new Project { Id = 1, UserId = "tenant-1", Name = "Tenant 1 Project" };
            var project2 = new Project { Id = 2, UserId = "tenant-2", Name = "Tenant 2 Project" };
            seedContext.Projects.AddRange(project1, project2);

            var todo1 = new Todo { Id = 10, ProjectId = 1, UserId = "tenant-1", Title = "Tenant 1 Task" };
            var todo2 = new Todo { Id = 20, ProjectId = 2, UserId = "tenant-2", Title = "Tenant 2 Task" };
            seedContext.Todos.AddRange(todo1, todo2);

            await seedContext.SaveChangesAsync();
        }

        // 2. Query as tenant-1
        await using (var tenant1Context = CreateContext(dbName, "tenant-1").Context)
        {
            var projects = await tenant1Context.Projects.ToListAsync();
            var todos = await tenant1Context.Todos.ToListAsync();

            Assert.Single(projects);
            Assert.Equal("Tenant 1 Project", projects[0].Name);
            Assert.Equal("tenant-1", projects[0].UserId);

            Assert.Single(todos);
            Assert.Equal("Tenant 1 Task", todos[0].Title);
            Assert.Equal("tenant-1", todos[0].UserId);
        }

        // 3. Query as tenant-2
        await using (var tenant2Context = CreateContext(dbName, "tenant-2").Context)
        {
            var projects = await tenant2Context.Projects.ToListAsync();
            var todos = await tenant2Context.Todos.ToListAsync();

            Assert.Single(projects);
            Assert.Equal("Tenant 2 Project", projects[0].Name);
            Assert.Equal("tenant-2", projects[0].UserId);

            Assert.Single(todos);
            Assert.Equal("Tenant 2 Task", todos[0].Title);
            Assert.Equal("tenant-2", todos[0].UserId);
        }
    }

    [Fact]
    public async Task GlobalQueryFilter_WhenUserIdIsNull_ReturnsEmptyLists()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();

        await using (var seedContext = CreateContext(dbName, "tenant-1").Context)
        {
            seedContext.Projects.Add(new Project { Id = 1, UserId = "tenant-1", Name = "Project 1" });
            seedContext.Todos.Add(new Todo { Id = 1, ProjectId = 1, UserId = "tenant-1", Title = "Todo 1" });
            await seedContext.SaveChangesAsync();
        }

        // Act - Query with null UserId
        await using (var nullUserContext = CreateContext(dbName, currentUserId: null).Context)
        {
            var projects = await nullUserContext.Projects.ToListAsync();
            var todos = await nullUserContext.Todos.ToListAsync();

            // Assert
            Assert.Empty(projects);
            Assert.Empty(todos);
        }
    }

    [Fact]
    public async Task SoftDelete_SaveChangesAsync_MarksProjectAsDeleted_AndUpdatesTimestamp()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var initialTime = DateTime.UtcNow.AddMinutes(-10);

        await using (var context = CreateContext(dbName, "tenant-1").Context)
        {
            var project = new Project
            {
                Id = 100,
                UserId = "tenant-1",
                Name = "Project to Delete",
                IsDeleted = false,
                UpdatedAt = initialTime
            };
            context.Projects.Add(project);
            await context.SaveChangesAsync();
        }

        // Act - Remove project and SaveChangesAsync
        await using (var context = CreateContext(dbName, "tenant-1").Context)
        {
            var project = await context.Projects.FirstAsync(p => p.Id == 100);
            context.Projects.Remove(project);
            var affectedRows = await context.SaveChangesAsync();

            Assert.Equal(1, affectedRows);
        }

        // Assert - Normal query filters out the soft-deleted project
        await using (var context = CreateContext(dbName, "tenant-1").Context)
        {
            var activeProjects = await context.Projects.ToListAsync();
            Assert.Empty(activeProjects);

            // Assert - IgnoreQueryFilters reveals the entity with IsDeleted = true
            var deletedProject = await context.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == 100);
            Assert.NotNull(deletedProject);
            Assert.True(deletedProject.IsDeleted);
            Assert.True(deletedProject.UpdatedAt > initialTime);
        }
    }

    [Fact]
    public async Task SoftDelete_SaveChangesAsync_MarksTodoAsDeleted_AndUpdatesTimestamp()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var initialTime = DateTime.UtcNow.AddMinutes(-5);

        await using (var context = CreateContext(dbName, "tenant-1").Context)
        {
            var todo = new Todo
            {
                Id = 200,
                ProjectId = 1,
                UserId = "tenant-1",
                Title = "Todo to Delete",
                IsDeleted = false,
                UpdatedAt = initialTime
            };
            context.Todos.Add(todo);
            await context.SaveChangesAsync();
        }

        // Act - Remove todo and SaveChangesAsync
        await using (var context = CreateContext(dbName, "tenant-1").Context)
        {
            var todo = await context.Todos.FirstAsync(t => t.Id == 200);
            context.Remove(todo);
            var affectedRows = await context.SaveChangesAsync();

            Assert.Equal(1, affectedRows);
        }

        // Assert - Normal query does not find the todo
        await using (var context = CreateContext(dbName, "tenant-1").Context)
        {
            var activeTodos = await context.Todos.ToListAsync();
            Assert.Empty(activeTodos);

            // IgnoreQueryFilters shows the todo is soft deleted
            var deletedTodo = await context.Todos.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == 200);
            Assert.NotNull(deletedTodo);
            Assert.True(deletedTodo.IsDeleted);
            Assert.True(deletedTodo.UpdatedAt > initialTime);
        }
    }

    [Fact]
    public void SoftDelete_SaveChanges_Synchronous_MarksEntitiesAsDeleted()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var initialTime = DateTime.UtcNow.AddHours(-1);

        using (var context = CreateContext(dbName, "tenant-1").Context)
        {
            context.Projects.Add(new Project
            {
                Id = 300,
                UserId = "tenant-1",
                Name = "Sync Project",
                IsDeleted = false,
                UpdatedAt = initialTime
            });
            context.Todos.Add(new Todo
            {
                Id = 301,
                ProjectId = 300,
                UserId = "tenant-1",
                Title = "Sync Todo",
                IsDeleted = false,
                UpdatedAt = initialTime
            });
            context.SaveChanges();
        }

        // Act - Remove using synchronous SaveChanges
        using (var context = CreateContext(dbName, "tenant-1").Context)
        {
            var project = context.Projects.First(p => p.Id == 300);
            var todo = context.Todos.First(t => t.Id == 301);

            context.Projects.Remove(project);
            context.Todos.Remove(todo);
            var affectedRows = context.SaveChanges();

            Assert.Equal(2, affectedRows);
        }

        // Assert - Both entities soft deleted and hidden from normal query
        using (var context = CreateContext(dbName, "tenant-1").Context)
        {
            Assert.Empty(context.Projects.ToList());
            Assert.Empty(context.Todos.ToList());

            var rawProjects = context.Projects.IgnoreQueryFilters().Where(p => p.Id == 300).ToList();
            var rawTodos = context.Todos.IgnoreQueryFilters().Where(t => t.Id == 301).ToList();

            Assert.Single(rawProjects);
            Assert.True(rawProjects[0].IsDeleted);
            Assert.True(rawProjects[0].UpdatedAt > initialTime);

            Assert.Single(rawTodos);
            Assert.True(rawTodos[0].IsDeleted);
            Assert.True(rawTodos[0].UpdatedAt > initialTime);
        }
    }

    [Fact]
    public async Task IgnoreQueryFilters_ReturnsAllTenantsAndDeletedRecords()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();

        await using (var context = CreateContext(dbName, "tenant-1").Context)
        {
            context.Projects.AddRange(
                new Project { Id = 1, UserId = "tenant-1", Name = "Active T1", IsDeleted = false },
                new Project { Id = 2, UserId = "tenant-1", Name = "Deleted T1", IsDeleted = true },
                new Project { Id = 3, UserId = "tenant-2", Name = "Active T2", IsDeleted = false },
                new Project { Id = 4, UserId = "tenant-2", Name = "Deleted T2", IsDeleted = true }
            );
            await context.SaveChangesAsync();
        }

        // Act & Assert
        await using (var context = CreateContext(dbName, "tenant-1").Context)
        {
            // Standard query applies filter: user_id == tenant-1 && !is_deleted
            var filtered = await context.Projects.ToListAsync();
            Assert.Single(filtered);
            Assert.Equal("Active T1", filtered[0].Name);

            // IgnoreQueryFilters returns all 4 records regardless of tenant or soft delete status
            var unfiltered = await context.Projects.IgnoreQueryFilters().ToListAsync();
            Assert.Equal(4, unfiltered.Count);
        }
    }
}
