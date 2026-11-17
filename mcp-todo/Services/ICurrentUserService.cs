namespace mcp_todo.Services;

public interface ICurrentUserService
{
    string? UserId { get; }
    string GetRequiredUserId();
}
