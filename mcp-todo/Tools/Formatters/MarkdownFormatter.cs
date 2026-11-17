using System.Text;
using mcp_todo.Models;

namespace mcp_todo.Tools.Formatters;

public class ProjectTodoGrouping : IGrouping<Project, Todo>
{
    private readonly List<Todo> _todos;

    public Project Key { get; }

    public ProjectTodoGrouping(Project project, IEnumerable<Todo> todos)
    {
        Key = project ?? throw new ArgumentNullException(nameof(project));
        _todos = todos?.ToList() ?? new List<Todo>();
    }

    public IEnumerator<Todo> GetEnumerator() => _todos.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public static class MarkdownFormatter
{
    /// <summary>
    /// Görevleri okunaklı bir Markdown tablosuna dönüştürür (per D-13).
    /// Kolonlar: | ID | Durum | Görev | Bitiş Tarihi |
    /// </summary>
    public static string FormatTodoList(IEnumerable<Todo> todos, string? projectTitle = null)
    {
        var todoList = todos as IList<Todo> ?? todos.ToList();
        if (todoList.Count == 0)
        {
            return "Bu projede listelenecek görev bulunamadı.";
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(projectTitle))
        {
            if (!projectTitle.StartsWith("#"))
            {
                sb.AppendLine($"### {projectTitle}");
            }
            else
            {
                sb.AppendLine(projectTitle);
            }
        }

        sb.AppendLine("| ID | Durum | Görev | Bitiş Tarihi |");
        sb.AppendLine("|---|---|---|---|");

        foreach (var todo in todoList)
        {
            var status = todo.IsCompleted ? "[x] Tamamlandı" : "[ ] Açık";
            var dueDate = todo.DueDate.HasValue ? todo.DueDate.Value.ToString("yyyy-MM-dd") : "-";
            var title = todo.Title.Replace("|", "\\|");
            sb.AppendLine($"| {todo.Id} | {status} | {title} | {dueDate} |");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Görevleri her bir proje için '### Proje: {p.Name} (ID: {p.Id})' başlığı altında
    /// ayrı Markdown tabloları olarak formatlar (per D-13, D-18).
    /// </summary>
    public static string FormatGroupedTodoList(IEnumerable<IGrouping<Project, Todo>> groupedTodos)
    {
        var sb = new StringBuilder();
        var anyGroup = false;

        foreach (var group in groupedTodos)
        {
            anyGroup = true;
            var project = group.Key;
            var todos = group.ToList();

            sb.AppendLine($"### Proje: {project.Name} (ID: {project.Id})");
            if (todos.Count == 0)
            {
                sb.AppendLine("Bu projede listelenecek görev bulunamadı.");
            }
            else
            {
                sb.AppendLine(FormatTodoList(todos));
            }
            sb.AppendLine();
        }

        if (!anyGroup)
        {
            return "Henüz kayıtlı bir göreviniz bulunmuyor.";
        }

        return sb.ToString().TrimEnd();
    }
}
