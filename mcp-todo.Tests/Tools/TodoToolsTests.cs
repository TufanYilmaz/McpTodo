using Microsoft.EntityFrameworkCore;
using Moq;
using mcp_todo.Data;
using mcp_todo.Models;
using mcp_todo.Services;
using mcp_todo.Tools;
using Xunit;

namespace mcp_todo.Tests.Tools;

public class TodoToolsTests
{
    private static (TodoTools Tools, AppDbContext Context, Mock<ICurrentUserService> UserMock) CreateTools(
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
        var tools = new TodoTools(context, userMock.Object);

        return (tools, context, userMock);
    }

    [Fact]
    public void Constructor_WithNullDbContext_ThrowsArgumentNullException()
    {
        // Arrange
        var userMock = new Mock<ICurrentUserService>();

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new TodoTools(null!, userMock.Object));
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
        var ex = Assert.Throws<ArgumentNullException>(() => new TodoTools(context, null!));
        Assert.Equal("currentUserService", ex.ParamName);
    }

    [Fact]
    public async Task ListTodosAsync_WithNonExistentProjectId_ReturnsD22GenericErrorMessage()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            // Act
            var result = await tools.ListTodosAsync(projectId: 999);

            // Assert (per D-18, D-22, MCP-04)
            Assert.Equal("Kayıt bulunamadı veya erişim yetkiniz yok (ID: 999)", result);
        }
    }

    [Fact]
    public async Task ListTodosAsync_WithSpecificProjectId_WhenProjectHasNoTodos_ReturnsEmptyMessage()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            var project = new Project { Id = 1, UserId = "tenant-test", Name = "Boş Proje" };
            context.Projects.Add(project);
            await context.SaveChangesAsync();

            // Act
            var result = await tools.ListTodosAsync(projectId: 1);

            // Assert (per D-18, MCP-04)
            Assert.Equal("Bu projede listelenecek görev bulunamadı.", result);
        }
    }

    [Fact]
    public async Task ListTodosAsync_WithSpecificProjectId_FiltersCompletedAndSoftDeletedTodos()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            var project = new Project { Id = 1, UserId = "tenant-test", Name = "Proje 1" };
            context.Projects.Add(project);

            context.Todos.AddRange(
                new Todo { Id = 10, ProjectId = 1, UserId = "tenant-test", Title = "Açık Görev", IsCompleted = false, IsDeleted = false },
                new Todo { Id = 11, ProjectId = 1, UserId = "tenant-test", Title = "Tamamlanmış Görev", IsCompleted = true, IsDeleted = false },
                new Todo { Id = 12, ProjectId = 1, UserId = "tenant-test", Title = "Silinmiş Görev", IsCompleted = false, IsDeleted = true }
            );

            await context.SaveChangesAsync();

            // Act: includeCompleted = false (default)
            var resultOpenOnly = await tools.ListTodosAsync(projectId: 1, includeCompleted: false);

            // Assert: only open task is visible
            Assert.Contains("Açık Görev", resultOpenOnly);
            Assert.DoesNotContain("Tamamlanmış Görev", resultOpenOnly);
            Assert.DoesNotContain("Silinmiş Görev", resultOpenOnly);

            // Act: includeCompleted = true
            var resultAll = await tools.ListTodosAsync(projectId: 1, includeCompleted: true);

            // Assert: open and completed tasks visible, soft deleted still hidden
            Assert.Contains("Açık Görev", resultAll);
            Assert.Contains("Tamamlanmış Görev", resultAll);
            Assert.DoesNotContain("Silinmiş Görev", resultAll);
        }
    }

    [Fact]
    public async Task ListTodosAsync_WithoutProjectId_WhenNoTodosExist_ReturnsEmptyMessage()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            // Act
            var result = await tools.ListTodosAsync(projectId: null);

            // Assert (per D-18, MCP-04)
            Assert.Equal("Henüz kayıtlı bir göreviniz bulunmuyor.", result);
        }
    }

    [Fact]
    public async Task ListTodosAsync_WithoutProjectId_ReturnsGroupedTodoList()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            var p1 = new Project { Id = 1, UserId = "tenant-test", Name = "İş Projesi" };
            var p2 = new Project { Id = 2, UserId = "tenant-test", Name = "Ev Projesi" };
            var p3 = new Project { Id = 3, UserId = "tenant-test", Name = "Boş Proje" };
            context.Projects.AddRange(p1, p2, p3);

            context.Todos.AddRange(
                new Todo { Id = 100, ProjectId = 1, UserId = "tenant-test", Title = "Rapor Hazırla", IsCompleted = false },
                new Todo { Id = 200, ProjectId = 2, UserId = "tenant-test", Title = "Alışveriş Yap", IsCompleted = true }
            );

            await context.SaveChangesAsync();

            // Act: includeCompleted = false
            var resultOpenOnly = await tools.ListTodosAsync(projectId: null, includeCompleted: false);

            // Assert: only p1 shown with its open task
            Assert.Contains("### Proje: İş Projesi (ID: 1)", resultOpenOnly);
            Assert.Contains("Rapor Hazırla", resultOpenOnly);
            Assert.DoesNotContain("Ev Projesi", resultOpenOnly);
            Assert.DoesNotContain("Boş Proje", resultOpenOnly);

            // Act: includeCompleted = true
            var resultAll = await tools.ListTodosAsync(projectId: null, includeCompleted: true);

            // Assert: both p1 and p2 shown
            Assert.Contains("### Proje: İş Projesi (ID: 1)", resultAll);
            Assert.Contains("Rapor Hazırla", resultAll);
            Assert.Contains("### Proje: Ev Projesi (ID: 2)", resultAll);
            Assert.Contains("Alışveriş Yap", resultAll);
            Assert.DoesNotContain("Boş Proje", resultAll);
        }
    }

    [Fact]
    public async Task AddTodoAsync_WithValidParameters_CreatesTodoAndReturnsSuccess()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            var project = new Project { Id = 1, UserId = "tenant-test", Name = "Proje 1" };
            context.Projects.Add(project);
            await context.SaveChangesAsync();

            // Act
            var result = await tools.AddTodoAsync(1, "E-posta Gönder", "2026-12-31");

            // Assert (per D-19, MCP-05)
            var todo = await context.Todos.FirstOrDefaultAsync(t => t.Title == "E-posta Gönder");
            Assert.NotNull(todo);
            Assert.Equal(1, todo.ProjectId);
            Assert.Equal("tenant-test", todo.UserId);
            Assert.False(todo.IsCompleted);
            Assert.False(todo.IsDeleted);
            Assert.NotNull(todo.DueDate);
            Assert.Equal(2026, todo.DueDate.Value.Year);
            Assert.Equal(12, todo.DueDate.Value.Month);
            Assert.Equal(31, todo.DueDate.Value.Day);
            Assert.Equal($"Başarılı: Görev oluşturuldu (ID: {todo.Id})", result);
        }
    }

    [Fact]
    public async Task AddTodoAsync_WithIsoDateString_ParsesDateCorrectly()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            var project = new Project { Id = 1, UserId = "tenant-test", Name = "Proje 1" };
            context.Projects.Add(project);
            await context.SaveChangesAsync();

            // Act
            var result = await tools.AddTodoAsync(1, "Toplantı", "2026-06-15T10:00:00Z");

            // Assert
            var todo = await context.Todos.FirstOrDefaultAsync(t => t.Title == "Toplantı");
            Assert.NotNull(todo);
            Assert.NotNull(todo.DueDate);
            Assert.Equal(DateTimeKind.Utc, todo.DueDate.Value.Kind);
            Assert.Equal(2026, todo.DueDate.Value.Year);
            Assert.Equal(6, todo.DueDate.Value.Month);
            Assert.Equal(15, todo.DueDate.Value.Day);
            Assert.Equal($"Başarılı: Görev oluşturuldu (ID: {todo.Id})", result);
        }
    }

    [Fact]
    public async Task AddTodoAsync_WithoutDueDate_CreatesTodoWithNullDueDate()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            var project = new Project { Id = 1, UserId = "tenant-test", Name = "Proje 1" };
            context.Projects.Add(project);
            await context.SaveChangesAsync();

            // Act
            var result = await tools.AddTodoAsync(1, "Tarihsiz Görev", null);

            // Assert
            var todo = await context.Todos.FirstOrDefaultAsync(t => t.Title == "Tarihsiz Görev");
            Assert.NotNull(todo);
            Assert.Null(todo.DueDate);
            Assert.Equal($"Başarılı: Görev oluşturuldu (ID: {todo.Id})", result);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AddTodoAsync_WithEmptyOrWhitespaceTitle_ReturnsErrorMessage(string? invalidTitle)
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            // Act
            var result = await tools.AddTodoAsync(1, invalidTitle!, "2026-12-31");

            // Assert (per D-19)
            Assert.Equal("Hata: Görev başlığı boş olamaz.", result);
            Assert.Empty(await context.Todos.ToListAsync());
        }
    }

    [Fact]
    public async Task AddTodoAsync_WithNonExistentProjectId_ReturnsD22GenericErrorMessage()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            // Act
            var result = await tools.AddTodoAsync(999, "Yetkisiz Görev", "2026-12-31");

            // Assert (per D-19, D-22, MCP-05)
            Assert.Equal("Kayıt bulunamadı veya erişim yetkiniz yok (ID: 999)", result);
            Assert.Empty(await context.Todos.ToListAsync());
        }
    }

    [Fact]
    public async Task AddTodoAsync_WithInvalidDateFormat_ReturnsErrorMessage()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            var project = new Project { Id = 1, UserId = "tenant-test", Name = "Proje 1" };
            context.Projects.Add(project);
            await context.SaveChangesAsync();

            // Act
            var result = await tools.AddTodoAsync(1, "Format Hatası Testi", "31-12-2026-gecersiz");

            // Assert (per D-19)
            Assert.Equal("Geçersiz tarih formatı. Lütfen YYYY-MM-DD formatında giriniz.", result);
            Assert.Empty(await context.Todos.ToListAsync());
        }
    }

    [Fact]
    public async Task CompleteTodoAsync_WithExistingTodo_MarksAsCompletedAndUpdatesTimestamp()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        var initialTime = DateTime.UtcNow.AddHours(-1);

        await using (context)
        {
            var project = new Project { Id = 1, UserId = "tenant-test", Name = "Proje 1" };
            var todo = new Todo
            {
                Id = 50,
                ProjectId = 1,
                UserId = "tenant-test",
                Title = "Tamamlanacak Görev",
                IsCompleted = false,
                UpdatedAt = initialTime
            };
            context.Projects.Add(project);
            context.Todos.Add(todo);
            await context.SaveChangesAsync();

            // Act
            var result = await tools.CompleteTodoAsync(50);

            // Assert (per D-20, MCP-06)
            Assert.Equal("Başarılı: Görev tamamlandı (ID: 50)", result);

            var updatedTodo = await context.Todos.FirstAsync(t => t.Id == 50);
            Assert.True(updatedTodo.IsCompleted);
            Assert.True(updatedTodo.UpdatedAt > initialTime);
        }
    }

    [Fact]
    public async Task CompleteTodoAsync_WithNonExistentTodoId_ReturnsD22GenericErrorMessage()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            // Act
            var result = await tools.CompleteTodoAsync(999);

            // Assert (per D-20, D-22, MCP-06)
            Assert.Equal("Kayıt bulunamadı veya erişim yetkiniz yok (ID: 999)", result);
        }
    }

    [Fact]
    public async Task DeleteTodoAsync_WithExistingTodo_SoftDeletesAndUpdatesTimestamp()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        var initialTime = DateTime.UtcNow.AddHours(-1);

        await using (context)
        {
            var project = new Project { Id = 1, UserId = "tenant-test", Name = "Proje 1" };
            var todo = new Todo
            {
                Id = 60,
                ProjectId = 1,
                UserId = "tenant-test",
                Title = "Silinecek Görev",
                IsDeleted = false,
                UpdatedAt = initialTime
            };
            context.Projects.Add(project);
            context.Todos.Add(todo);
            await context.SaveChangesAsync();

            // Act
            var result = await tools.DeleteTodoAsync(60);

            // Assert (per D-21, MCP-07)
            Assert.Equal("Başarılı: Görev silindi (ID: 60)", result);

            // Regular query excludes soft-deleted todo
            Assert.Empty(await context.Todos.ToListAsync());

            // IgnoreQueryFilters shows entity marked as deleted
            var rawTodo = await context.Todos.IgnoreQueryFilters().FirstAsync(t => t.Id == 60);
            Assert.True(rawTodo.IsDeleted);
            Assert.True(rawTodo.UpdatedAt > initialTime);
        }
    }

    [Fact]
    public async Task DeleteTodoAsync_WithNonExistentTodoId_ReturnsD22GenericErrorMessage()
    {
        // Arrange
        var (tools, context, _) = CreateTools();
        await using (context)
        {
            // Act
            var result = await tools.DeleteTodoAsync(999);

            // Assert (per D-21, D-22, MCP-07)
            Assert.Equal("Kayıt bulunamadı veya erişim yetkiniz yok (ID: 999)", result);
        }
    }
}
