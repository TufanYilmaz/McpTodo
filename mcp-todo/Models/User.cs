namespace mcp_todo.Models;

public class User
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation collections
    public ICollection<Project> Projects { get; set; } = new List<Project>();
    public ICollection<Todo> Todos { get; set; } = new List<Todo>();
}
