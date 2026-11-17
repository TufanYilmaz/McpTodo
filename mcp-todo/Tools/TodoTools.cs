using System.ComponentModel;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using mcp_todo.Data;
using mcp_todo.Models;
using mcp_todo.Services;
using mcp_todo.Tools.Formatters;

namespace mcp_todo.Tools;

[McpServerToolType]
public class TodoTools
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUserService;

    public TodoTools(AppDbContext db, ICurrentUserService currentUserService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    }

    [McpServerTool(Name = "todo_list", Title = "Görevleri Listele")]
    [Description("Giriş yapmış kullanıcının görevlerini tek bir proje bazında veya tüm projeler bazında gruplanmış Markdown tablosu olarak listeler.")]
    public async Task<string> ListTodosAsync(
        [Description("Filtrelenecek proje ID'si (isteğe bağlı)")] long? projectId = null,
        [Description("Tamamlanmış görevleri dahil et (varsayılan: false)")] bool includeCompleted = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (projectId.HasValue)
            {
                var project = await _db.Projects
                    .FirstOrDefaultAsync(p => p.Id == projectId.Value, cancellationToken);

                if (project == null)
                {
                    return $"Kayıt bulunamadı veya erişim yetkiniz yok (ID: {projectId.Value})";
                }

                var query = _db.Todos
                    .Where(t => t.ProjectId == projectId.Value && !t.IsDeleted);

                if (!includeCompleted)
                {
                    query = query.Where(t => !t.IsCompleted);
                }

                var todos = await query
                    .OrderBy(t => t.Id)
                    .ToListAsync(cancellationToken);

                if (todos.Count == 0)
                {
                    return "Bu projede listelenecek görev bulunamadı.";
                }

                return MarkdownFormatter.FormatTodoList(todos);
            }
            else
            {
                var projects = await _db.Projects
                    .Include(p => p.Todos)
                    .OrderBy(p => p.Id)
                    .ToListAsync(cancellationToken);

                var groupedList = new List<IGrouping<Project, Todo>>();
                var totalCount = 0;

                foreach (var project in projects)
                {
                    var matchingTodos = project.Todos
                        .Where(t => !t.IsDeleted && (includeCompleted || !t.IsCompleted))
                        .OrderBy(t => t.Id)
                        .ToList();

                    if (matchingTodos.Count > 0)
                    {
                        groupedList.Add(new ProjectTodoGrouping(project, matchingTodos));
                        totalCount += matchingTodos.Count;
                    }
                }

                if (totalCount == 0)
                {
                    return "Henüz kayıtlı bir göreviniz bulunmuyor.";
                }

                return MarkdownFormatter.FormatGroupedTodoList(groupedList);
            }
        }
        catch (mcp_todo.Exceptions.UnauthorizedException ex)
        {
            Console.Error.WriteLine($"[TodoTools.ListTodosAsync] Unauthorized: {ex.Message}");
            return $"Hata: Kimlik doğrulama başarısız oldu ({ex.Message}). Lütfen oturum açtığınızdan emin olun.";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TodoTools.ListTodosAsync] Error: {ex}");
            return $"Hata: Görevler listelenirken bir hata oluştu ({ex.Message})";
        }
    }

    [McpServerTool(Name = "todo_add", Title = "Yeni Görev Ekle")]
    [Description("Belirtilen projeye yeni bir görev ekler.")]
    public async Task<string> AddTodoAsync(
        [Description("Görevin ekleneceği proje ID'si (zorunlu)")] long projectId,
        [Description("Görev başlığı (zorunlu)")] string title,
        [Description("Bitiş tarihi (isteğe bağlı, YYYY-MM-DD veya ISO formatında)")] string? dueDate = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Hata: Görev başlığı boş olamaz.";
        }

        try
        {
            var project = await _db.Projects
                .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

            if (project == null)
            {
                return $"Kayıt bulunamadı veya erişim yetkiniz yok (ID: {projectId})";
            }

            DateTime? parsedDueDate = null;
            if (!string.IsNullOrWhiteSpace(dueDate))
            {
                if (DateTime.TryParse(dueDate, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
                {
                    parsedDueDate = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                }
                else
                {
                    return "Geçersiz tarih formatı. Lütfen YYYY-MM-DD formatında giriniz.";
                }
            }

            var userId = _currentUserService.GetRequiredUserId();
            var todo = new Todo
            {
                ProjectId = projectId,
                UserId = userId,
                Title = title.Trim(),
                DueDate = parsedDueDate,
                IsCompleted = false,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Todos.Add(todo);
            await _db.SaveChangesAsync(cancellationToken);

            return $"Başarılı: Görev oluşturuldu (ID: {todo.Id})";
        }
        catch (mcp_todo.Exceptions.UnauthorizedException ex)
        {
            Console.Error.WriteLine($"[TodoTools.AddTodoAsync] Unauthorized: {ex.Message}");
            return $"Hata: Kimlik doğrulama başarısız oldu ({ex.Message}). Lütfen oturum açtığınızdan emin olun.";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TodoTools.AddTodoAsync] Error: {ex}");
            return $"Hata: Görev eklenirken bir hata oluştu ({ex.Message})";
        }
    }

    [McpServerTool(Name = "todo_complete", Title = "Görevi Tamamla")]
    [Description("Belirtilen görevi tamamlandı olarak işaretler.")]
    public async Task<string> CompleteTodoAsync(
        [Description("Tamamlanacak görevin kimlik numarası (ID)")] long todoId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var todo = await _db.Todos
                .FirstOrDefaultAsync(t => t.Id == todoId, cancellationToken);

            if (todo == null)
            {
                return $"Kayıt bulunamadı veya erişim yetkiniz yok (ID: {todoId})";
            }

            todo.IsCompleted = true;
            todo.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            return $"Başarılı: Görev tamamlandı (ID: {todoId})";
        }
        catch (mcp_todo.Exceptions.UnauthorizedException ex)
        {
            Console.Error.WriteLine($"[TodoTools.CompleteTodoAsync] Unauthorized: {ex.Message}");
            return $"Hata: Kimlik doğrulama başarısız oldu ({ex.Message}).";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TodoTools.CompleteTodoAsync] Error: {ex}");
            return $"Hata: Görev tamamlanırken bir hata oluştu ({ex.Message})";
        }
    }

    [McpServerTool(Name = "todo_delete", Title = "Görevi Sil")]
    [Description("Belirtilen görevi soft-delete ile siler.")]
    public async Task<string> DeleteTodoAsync(
        [Description("Silinecek görevin kimlik numarası (ID)")] long todoId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var todo = await _db.Todos
                .FirstOrDefaultAsync(t => t.Id == todoId, cancellationToken);

            if (todo == null)
            {
                return $"Kayıt bulunamadı veya erişim yetkiniz yok (ID: {todoId})";
            }

            todo.IsDeleted = true;
            todo.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            return $"Başarılı: Görev silindi (ID: {todoId})";
        }
        catch (mcp_todo.Exceptions.UnauthorizedException ex)
        {
            Console.Error.WriteLine($"[TodoTools.DeleteTodoAsync] Unauthorized: {ex.Message}");
            return $"Hata: Kimlik doğrulama başarısız oldu ({ex.Message}).";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TodoTools.DeleteTodoAsync] Error: {ex}");
            return $"Hata: Görev silinirken bir hata oluştu ({ex.Message})";
        }
    }
}
