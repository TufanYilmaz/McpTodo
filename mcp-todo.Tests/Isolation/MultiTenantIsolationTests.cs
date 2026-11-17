using Microsoft.EntityFrameworkCore;
using Moq;
using mcp_todo.Data;
using mcp_todo.Models;
using mcp_todo.Services;
using mcp_todo.Tools;
using Xunit;

namespace mcp_todo.Tests.Isolation;

public class MultiTenantIsolationTests
{
    private static (AppDbContext Context, ProjectTools ProjectTools, TodoTools TodoTools) CreateTenantEnvironment(
        string dbName,
        string tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        var userMock = new Mock<ICurrentUserService>();
        userMock.Setup(u => u.UserId).Returns(tenantId);
        userMock.Setup(u => u.GetRequiredUserId()).Returns(tenantId);

        var context = new AppDbContext(options, userMock.Object);
        var projectTools = new ProjectTools(context, userMock.Object);
        var todoTools = new TodoTools(context, userMock.Object);

        return (context, projectTools, todoTools);
    }

    [Fact]
    public async Task Scenario1_ProjectListing_TenantBCannotSeeTenantAProjects()
    {
        // Arrange (shared database, two distinct tenant sessions)
        var dbName = Guid.NewGuid().ToString();
        var (ctxA, pToolsA, _) = CreateTenantEnvironment(dbName, "tenant-a");
        var (ctxB, pToolsB, _) = CreateTenantEnvironment(dbName, "tenant-b");

        await using (ctxA)
        await using (ctxB)
        {
            // Tenant A creates a project
            var createResult = await pToolsA.CreateProjectAsync("Tenant A Gizli Proje", "Tenant A Açıklama");
            Assert.StartsWith("Başarılı: Proje oluşturuldu", createResult);

            // Act: Tenant B lists projects
            var listResultB = await pToolsB.ListProjectsAsync();

            // Assert: Tenant B sees no projects and receives default empty message (D-38, TEST-03)
            Assert.Equal("Henüz kayıtlı bir projeniz bulunmuyor. Yeni bir proje oluşturmak için 'project_create' aracını kullanabilirsiniz.", listResultB);
        }
    }

    [Fact]
    public async Task Scenario2_TodoListing_TenantBCannotListTenantATodos()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var (ctxA, pToolsA, tToolsA) = CreateTenantEnvironment(dbName, "tenant-a");
        var (ctxB, _, tToolsB) = CreateTenantEnvironment(dbName, "tenant-b");

        await using (ctxA)
        await using (ctxB)
        {
            await pToolsA.CreateProjectAsync("Tenant A Projesi");
            var projectA = await ctxA.Projects.FirstAsync();
            await tToolsA.AddTodoAsync(projectA.Id, "Tenant A Görevi 1");
            await tToolsA.AddTodoAsync(projectA.Id, "Tenant A Görevi 2");

            // Act 1: Tenant B lists todos for Tenant A's project
            var listByProjectB = await tToolsB.ListTodosAsync(projectId: projectA.Id);

            // Assert 1: D-22 generic error message prevents disclosing project existence
            Assert.Equal($"Kayıt bulunamadı veya erişim yetkiniz yok (ID: {projectA.Id})", listByProjectB);

            // Act 2: Tenant B lists all todos without project ID
            var listAllB = await tToolsB.ListTodosAsync(projectId: null);

            // Assert 2: Tenant B sees no todos
            Assert.Equal("Henüz kayıtlı bir göreviniz bulunmuyor.", listAllB);
        }
    }

    [Fact]
    public async Task Scenario3_CrossTenantTodoAdd_TenantBCannotAddTodoToTenantAProject()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var (ctxA, pToolsA, _) = CreateTenantEnvironment(dbName, "tenant-a");
        var (ctxB, _, tToolsB) = CreateTenantEnvironment(dbName, "tenant-b");

        await using (ctxA)
        await using (ctxB)
        {
            await pToolsA.CreateProjectAsync("Tenant A Projesi");
            var projectA = await ctxA.Projects.FirstAsync();

            // Act: Tenant B attempts to add a task to Tenant A's project
            var addResultB = await tToolsB.AddTodoAsync(projectA.Id, "Yetkisiz Görev Ekleme");

            // Assert: D-22 error returned
            Assert.Equal($"Kayıt bulunamadı veya erişim yetkiniz yok (ID: {projectA.Id})", addResultB);

            // Assert: In Tenant A's context, no tasks were added
            var todosA = await ctxA.Todos.Where(t => t.ProjectId == projectA.Id).ToListAsync();
            Assert.Empty(todosA);
        }
    }

    [Fact]
    public async Task Scenario4_CrossTenantTodoComplete_TenantBCannotCompleteTenantATodo()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var (ctxA, pToolsA, tToolsA) = CreateTenantEnvironment(dbName, "tenant-a");
        var (ctxB, _, tToolsB) = CreateTenantEnvironment(dbName, "tenant-b");

        await using (ctxA)
        await using (ctxB)
        {
            await pToolsA.CreateProjectAsync("Tenant A Projesi");
            var projectA = await ctxA.Projects.FirstAsync();
            await tToolsA.AddTodoAsync(projectA.Id, "Tenant A Açık Görev");
            var todoA = await ctxA.Todos.FirstAsync();
            Assert.False(todoA.IsCompleted);

            // Act: Tenant B attempts to complete Tenant A's task
            var completeResultB = await tToolsB.CompleteTodoAsync(todoA.Id);

            // Assert: D-22 error returned
            Assert.Equal($"Kayıt bulunamadı veya erişim yetkiniz yok (ID: {todoA.Id})", completeResultB);

            // Assert: Tenant A's task remains incomplete
            var refreshedTodoA = await ctxA.Todos.FirstAsync(t => t.Id == todoA.Id);
            Assert.False(refreshedTodoA.IsCompleted);
        }
    }

    [Fact]
    public async Task Scenario5_CrossTenantTodoDelete_TenantBCannotDeleteTenantATodo()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var (ctxA, pToolsA, tToolsA) = CreateTenantEnvironment(dbName, "tenant-a");
        var (ctxB, _, tToolsB) = CreateTenantEnvironment(dbName, "tenant-b");

        await using (ctxA)
        await using (ctxB)
        {
            await pToolsA.CreateProjectAsync("Tenant A Projesi");
            var projectA = await ctxA.Projects.FirstAsync();
            await tToolsA.AddTodoAsync(projectA.Id, "Tenant A Silinmeyecek Görev");
            var todoA = await ctxA.Todos.FirstAsync();
            Assert.False(todoA.IsDeleted);

            // Act: Tenant B attempts to delete Tenant A's task
            var deleteResultB = await tToolsB.DeleteTodoAsync(todoA.Id);

            // Assert: D-22 error returned
            Assert.Equal($"Kayıt bulunamadı veya erişim yetkiniz yok (ID: {todoA.Id})", deleteResultB);

            // Assert: In Tenant A's context, the task remains active
            var activeTodosA = await ctxA.Todos.ToListAsync();
            Assert.Single(activeTodosA);
            Assert.Equal(todoA.Id, activeTodosA[0].Id);
            Assert.False(activeTodosA[0].IsDeleted);
        }
    }

    [Fact]
    public async Task Scenario6_CrossTenantProjectDelete_TenantBCannotDeleteTenantAProject()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var (ctxA, pToolsA, tToolsA) = CreateTenantEnvironment(dbName, "tenant-a");
        var (ctxB, pToolsB, _) = CreateTenantEnvironment(dbName, "tenant-b");

        await using (ctxA)
        await using (ctxB)
        {
            await pToolsA.CreateProjectAsync("Tenant A Projesi");
            var projectA = await ctxA.Projects.FirstAsync();
            await tToolsA.AddTodoAsync(projectA.Id, "Tenant A Görevi");

            // Act: Tenant B attempts to delete Tenant A's project
            var deleteResultB = await pToolsB.DeleteProjectAsync(projectA.Id);

            // Assert: D-22 error returned
            Assert.Equal($"Kayıt bulunamadı veya erişim yetkiniz yok (ID: {projectA.Id})", deleteResultB);

            // Assert: In Tenant A's context, the project and attached todo remain active
            var activeProjectsA = await ctxA.Projects.ToListAsync();
            var activeTodosA = await ctxA.Todos.ToListAsync();
            Assert.Single(activeProjectsA);
            Assert.False(activeProjectsA[0].IsDeleted);
            Assert.Single(activeTodosA);
            Assert.False(activeTodosA[0].IsDeleted);
        }
    }
}
