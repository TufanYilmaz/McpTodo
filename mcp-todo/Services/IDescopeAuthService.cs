using mcp_todo.Models;

namespace mcp_todo.Services;

public interface IDescopeAuthService
{
    Task<DescopeUserData> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);
}
