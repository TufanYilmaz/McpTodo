using System.ComponentModel;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using mcp_todo.Data;
using mcp_todo.Models;
using mcp_todo.Services;

namespace mcp_todo.Tools;

[McpServerToolType]
public class ProjectTools
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUserService;

    public ProjectTools(AppDbContext db, ICurrentUserService currentUserService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    }

    [McpServerTool(Name = "project_list", Title = "Projeleri Listele")]
    [Description("Giriş yapmış kullanıcının aktif projelerini ve görev sayılarını okunaklı bir Markdown tablosu olarak listeler.")]
    public async Task<string> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var projects = await _db.Projects
                .Include(p => p.Todos)
                .OrderBy(p => p.Id)
                .ToListAsync(cancellationToken);

            if (projects.Count == 0)
            {
                return "Henüz kayıtlı bir projeniz bulunmuyor. Yeni bir proje oluşturmak için 'project_create' aracını kullanabilirsiniz.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("| ID | Proje Adı | Açıklama | Görev Sayısı |");
            sb.AppendLine("|---|---|---|---|");

            foreach (var p in projects)
            {
                var activeTodoCount = p.Todos.Count(t => !t.IsDeleted);
                var description = string.IsNullOrWhiteSpace(p.Description) ? "-" : p.Description.Trim();
                sb.AppendLine($"| {p.Id} | {p.Name} | {description} | {activeTodoCount} |");
            }

            return sb.ToString().TrimEnd();
        }
        catch (mcp_todo.Exceptions.UnauthorizedException ex)
        {
            Console.Error.WriteLine($"[ProjectTools.ListProjectsAsync] Unauthorized: {ex.Message}");
            return $"Hata: Kimlik doğrulama başarısız oldu ({ex.Message}). Lütfen oturum açtığınızdan emin olun.";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ProjectTools.ListProjectsAsync] Error: {ex}");
            return $"Hata: Projeler listelenirken bir hata oluştu ({ex.Message})";
        }
    }

    [McpServerTool(Name = "project_create", Title = "Yeni Proje Oluştur")]
    [Description("Giriş yapmış kullanıcı için yeni bir proje oluşturur.")]
    public async Task<string> CreateProjectAsync(
        [Description("Proje adı (zorunlu)")] string name,
        [Description("Proje açıklaması (isteğe bağlı)")] string? description = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Hata: Proje adı boş olamaz.";
        }

        try
        {
            var userId = _currentUserService.GetRequiredUserId();
            var project = new Project
            {
                UserId = userId,
                Name = name.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Projects.Add(project);
            await _db.SaveChangesAsync(cancellationToken);

            return $"Başarılı: Proje oluşturuldu (ID: {project.Id})";
        }
        catch (mcp_todo.Exceptions.UnauthorizedException ex)
        {
            Console.Error.WriteLine($"[ProjectTools.CreateProjectAsync] Unauthorized: {ex.Message}");
            return $"Hata: Kimlik doğrulama başarısız oldu ({ex.Message}). Lütfen oturum açtığınızdan veya geçerli bir token sağladığınızdan emin olun.";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ProjectTools.CreateProjectAsync] Error: {ex}");
            return $"Hata: Proje oluşturulamadı ({ex.Message})";
        }
    }

    [McpServerTool(Name = "project_delete", Title = "Proje Sil")]
    [Description("Belirtilen projeyi ve altındaki tüm görevleri otomatik kaskad soft-delete ile siler.")]
    public async Task<string> DeleteProjectAsync(
        [Description("Silinecek projenin kimlik numarası (ID)")] long projectId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var project = await _db.Projects
                .Include(p => p.Todos)
                .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

            if (project == null)
            {
                return $"Kayıt bulunamadı veya erişim yetkiniz yok (ID: {projectId})";
            }

            var activeTodos = project.Todos.Where(t => !t.IsDeleted).ToList();
            var now = DateTime.UtcNow;

            foreach (var todo in activeTodos)
            {
                todo.IsDeleted = true;
                todo.UpdatedAt = now;
            }

            project.IsDeleted = true;
            project.UpdatedAt = now;

            await _db.SaveChangesAsync(cancellationToken);

            return $"Başarılı: Proje ve bağlı {activeTodos.Count} adet görev silindi (ID: {projectId})";
        }
        catch (mcp_todo.Exceptions.UnauthorizedException ex)
        {
            Console.Error.WriteLine($"[ProjectTools.DeleteProjectAsync] Unauthorized: {ex.Message}");
            return $"Hata: Kimlik doğrulama başarısız oldu ({ex.Message}).";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ProjectTools.DeleteProjectAsync] Error: {ex}");
            return $"Hata: Proje silinirken bir hata oluştu ({ex.Message})";
        }
    }
}
