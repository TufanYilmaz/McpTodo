using mcp_todo.Exceptions;
using mcp_todo.Services;
using Xunit;

namespace mcp_todo.Tests.Services;

public class DevCurrentUserServiceTests
{
    [Fact]
    public void Constructor_WithoutParameters_ShouldSetDefaultUserId()
    {
        // Act
        var service = new DevCurrentUserService();

        // Assert
        Assert.Equal(DevCurrentUserService.DefaultUserId, service.UserId);
        Assert.Equal("default-user", service.UserId);
    }

    [Fact]
    public void Constructor_WithCustomUserId_ShouldSetCustomUserId()
    {
        // Arrange
        const string expectedUserId = "custom-user-42";

        // Act
        var service = new DevCurrentUserService(expectedUserId);

        // Assert
        Assert.Equal(expectedUserId, service.UserId);
    }

    [Fact]
    public void Implements_ICurrentUserService_Interface()
    {
        // Act
        var service = new DevCurrentUserService();

        // Assert
        Assert.IsAssignableFrom<ICurrentUserService>(service);
    }

    [Fact]
    public void GetRequiredUserId_WithDefaultUserId_ShouldReturnDefaultUserId()
    {
        // Arrange
        var service = new DevCurrentUserService();

        // Act
        var result = service.GetRequiredUserId();

        // Assert
        Assert.Equal(DevCurrentUserService.DefaultUserId, result);
    }

    [Fact]
    public void GetRequiredUserId_WithValidCustomUserId_ShouldReturnCustomUserId()
    {
        // Arrange
        const string expectedUserId = "user-abc-123";
        var service = new DevCurrentUserService(expectedUserId);

        // Act
        var result = service.GetRequiredUserId();

        // Assert
        Assert.Equal(expectedUserId, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void GetRequiredUserId_WhenUserIdIsNullOrWhiteSpace_ShouldThrowUnauthorizedException(string? invalidUserId)
    {
        // Arrange
        var service = new DevCurrentUserService(invalidUserId);

        // Act & Assert
        var exception = Assert.Throws<UnauthorizedException>(() => service.GetRequiredUserId());
        Assert.Equal("Unauthorized: User ID is missing or invalid.", exception.Message);
    }
}
