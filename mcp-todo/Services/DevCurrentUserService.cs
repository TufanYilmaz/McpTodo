using mcp_todo.Exceptions;

namespace mcp_todo.Services;

public class DevCurrentUserService : ICurrentUserService
{
    public const string DefaultUserId = "default-user";
    private readonly string? _userId;

    public DevCurrentUserService(string? userId = DefaultUserId)
    {
        _userId = userId;
    }

    public string? UserId => _userId;

    public string GetRequiredUserId()
    {
        if (string.IsNullOrWhiteSpace(UserId))
        {
            throw new UnauthorizedException();
        }

        return UserId;
    }
}
