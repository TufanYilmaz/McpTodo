namespace mcp_todo.Exceptions;

public class UnauthorizedException : Exception
{
    private const string DefaultMessage = "Unauthorized: User ID is missing or invalid.";

    public UnauthorizedException() : base(DefaultMessage)
    {
    }

    public UnauthorizedException(string message) : base(message)
    {
    }

    public UnauthorizedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
