using Microsoft.EntityFrameworkCore;
using Moq;
using mcp_todo.Data;
using mcp_todo.Models;
using mcp_todo.Services;
using mcp_todo.Tools;
using Xunit;

namespace mcp_todo.Tests.Tools;

public class ProjectToolsTests
{
    private static (ProjectTools Tools, AppDbContext Context, Mock<ICurrentUserService> UserMock) CreateTools(
        string? currentUserId = "tenant-test",
        string? databaseName = null)
    {
        databaseName ??= Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: databaseName)
            .Options;

        var userMock = new Mock<ICurrentUserService>();
        userMock.Setup(u => u.UserId).Returns(currentUserId);
        userMock.Setup(u => u.GetRequiredUserId()).Returns(currentUserId ?? string.Empty);

        var context = new AppDbContext(options, userMock.Object);
        var tools = new ProjectTools(context, userMock.Object);

        return (tools, context, userMock);
    }

    [Fact]
    public void Constructor_WithNullDbContext_ThrowsArgumentNullException()
    {
        // Arrange
        var userMock = new Mock<ICurrentUserService>();

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new ProjectTools(null!, userMock.Object));
        Assert.Equal("db", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullCurrentUserService_ThrowsArgumentNullException()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var dummyUserMock = new Mock<ICurrentUserService>();
        using var context = new AppDbContext(options, dummyUserMock.Object);

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new ProjectTools(context, null!));
        Assert.Equal("currentUserService", ex.ParamName);
    }

    [Fact]
    public async Task ListProjectsAsync_WhenNoProjectsExist_ReturnsEmptyMessage()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            // Act
            var result = await tools.ListProjectsAsync();

            // Assert (per D-13, D-15, MCP-01)
            Assert.Equal("Henüz kayıtlı bir projeniz bulunmuyor. Yeni bir proje oluşturmak için 'project_create' aracını kullanabilirsiniz.", result);
        }
    }

    [Fact]
    public async Task ListProjectsAsync_WhenProjectsExist_ReturnsMarkdownTable_WithActiveTodoCounts()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            var p1 = new Project
            {
                Id = 1,
                UserId = "tenant-test",
                Name = "Alpha Projesi",
                Description = "Alpha açıklaması",
                IsDeleted = false
            };
            var p2 = new Project
            {
                Id = 2,
                UserId = "tenant-test",
                Name = "Beta Projesi",
                Description = null,
                IsDeleted = false
            };
            var p3 = new Project
            {
                Id = 3,
                UserId = "tenant-test",
                Name = "Gamma Projesi",
                Description = "   ",
                IsDeleted = false
            };

            context.Projects.AddRange(p1, p2, p3);

            // Add todos to p1: 2 active, 1 soft-deleted
            context.Todos.AddRange(
                new Todo { Id = 10, ProjectId = 1, UserId = "tenant-test", Title = "T1", IsDeleted = false },
                new Todo { Id = 11, ProjectId = 1, UserId = "tenant-test", Title = "T2", IsDeleted = false },
                new Todo { Id = 12, ProjectId = 1, UserId = "tenant-test", Title = "T3 Deleted", IsDeleted = true }
            );

            // Add 1 active todo to p3
            context.Todos.Add(new Todo { Id = 13, ProjectId = 3, UserId = "tenant-test", Title = "T4", IsDeleted = false });

            await context.SaveChangesAsync();

            // Act
            var result = await tools.ListProjectsAsync();

            // Assert (per D-15, D-40)
            Assert.Contains("| ID | Proje Adı | Açıklama | Görev Sayısı |", result);
            Assert.Contains("|---|---|---|---|", result);
            Assert.Contains("| 1 | Alpha Projesi | Alpha açıklaması | 2 |", result);
            Assert.Contains("| 2 | Beta Projesi | - | 0 |", result);
            Assert.Contains("| 3 | Gamma Projesi | - | 1 |", result);
        }
    }

    [Fact]
    public async Task CreateProjectAsync_WithValidNameAndDescription_CreatesProjectAndReturnsSuccess()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            // Act
            var result = await tools.CreateProjectAsync("Yeni Proje", "Proje Detayı");

            // Assert (per D-14, D-16, MCP-02)
            var project = await context.Projects.FirstOrDefaultAsync(p => p.Name == "Yeni Proje");
            Assert.NotNull(project);
            Assert.Equal("Proje Detayı", project.Description);
            Assert.Equal("tenant-test", project.UserId);
            Assert.False(project.IsDeleted);
            Assert.Equal($"Başarılı: Proje oluşturuldu (ID: {project.Id})", result);
        }
    }

    [Fact]
    public async Task CreateProjectAsync_WithNullOrWhitespaceDescription_SavesDescriptionAsNull()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            // Act
            var result = await tools.CreateProjectAsync("Açıklamasız Proje", "   ");

            // Assert
            var project = await context.Projects.FirstOrDefaultAsync(p => p.Name == "Açıklamasız Proje");
            Assert.NotNull(project);
            Assert.Null(project.Description);
            Assert.Equal($"Başarılı: Proje oluşturuldu (ID: {project.Id})", result);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CreateProjectAsync_WithEmptyOrWhitespaceName_ReturnsErrorMessage(string? invalidName)
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            // Act
            var result = await tools.CreateProjectAsync(invalidName!, "Açıklama");

            // Assert (per D-14, D-16)
            Assert.Equal("Hata: Proje adı boş olamaz.", result);
            Assert.Empty(await context.Projects.ToListAsync());
        }
    }

    [Fact]
    public async Task DeleteProjectAsync_WithExistingProject_CascadeSoftDeletesProjectAndTodos()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            var project = new Project
            {
                Id = 100,
                UserId = "tenant-test",
                Name = "Silinecek Proje",
                IsDeleted = false
            };
            context.Projects.Add(project);

            context.Todos.AddRange(
                new Todo { Id = 101, ProjectId = 100, UserId = "tenant-test", Title = "Görev 1", IsDeleted = false },
                new Todo { Id = 102, ProjectId = 100, UserId = "tenant-test", Title = "Görev 2", IsDeleted = false },
                new Todo { Id = 103, ProjectId = 100, UserId = "tenant-test", Title = "Önceden Silinmiş Görev", IsDeleted = true }
            );

            await context.SaveChangesAsync();

            // Act
            var result = await tools.DeleteProjectAsync(100);

            // Assert (per D-14, D-17, MCP-03)
            Assert.Equal("Başarılı: Proje ve bağlı 2 adet görev silindi (ID: 100)", result);

            // Normal query filters out deleted entities
            Assert.Empty(await context.Projects.ToListAsync());
            Assert.Empty(await context.Todos.ToListAsync());

            // IgnoreQueryFilters shows entities marked as IsDeleted = true
            var rawProject = await context.Projects.IgnoreQueryFilters().FirstAsync(p => p.Id == 100);
            Assert.True(rawProject.IsDeleted);

            var rawTodos = await context.Todos.IgnoreQueryFilters().Where(t => t.ProjectId == 100).ToListAsync();
            Assert.Equal(3, rawTodos.Count);
            Assert.All(rawTodos, t => Assert.True(t.IsDeleted));
        }
    }

    [Fact]
    public async Task DeleteProjectAsync_WithNonExistentProjectId_ReturnsD22GenericErrorMessage()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            // Act
            var result = await tools.DeleteProjectAsync(999);

            // Assert (per D-17, D-22, MCP-03)
            Assert.Equal("Kayıt bulunamadı veya erişim yetkiniz yok (ID: 999)", result);
        }
    }
}
