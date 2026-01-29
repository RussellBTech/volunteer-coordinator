using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VSMS.Core.Enums;
using VSMS.Infrastructure.Data;
using VSMS.Infrastructure.Services;

namespace VSMS.Tests.Unit;

public class TokenServiceTests
{
    private VsmsDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<VsmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new VsmsDbContext(options);
        return context;
    }

    private IConfiguration CreateConfiguration(string baseUrl = "https://example.com")
    {
        var configDict = new Dictionary<string, string?>
        {
            { "App:BaseUrl", baseUrl },
            { "App:TokenExpirationDays", "14" }
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();
    }

    [Fact]
    public async Task CreateTokenAsync_GeneratesUniqueToken()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var config = CreateConfiguration();
        var service = new TokenService(context, config);

        // Act
        var token1 = await service.CreateTokenAsync(1, 1, TokenAction.Confirm);
        var token2 = await service.CreateTokenAsync(1, 1, TokenAction.Confirm);

        // Assert
        Assert.NotNull(token1.Token);
        Assert.NotNull(token2.Token);
        Assert.NotEqual(token1.Token, token2.Token);
    }

    [Fact]
    public async Task CreateTokenAsync_SetsCorrectExpiration()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var config = CreateConfiguration();
        var service = new TokenService(context, config);
        var beforeCreate = DateTime.UtcNow;

        // Act
        var token = await service.CreateTokenAsync(1, 1, TokenAction.Confirm);

        // Assert
        Assert.True(token.ExpiresAt > beforeCreate.AddDays(13));
        Assert.True(token.ExpiresAt < beforeCreate.AddDays(15));
    }

    [Fact]
    public async Task CreateTokenAsync_WithCustomExpiration_UsesCustomDays()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var config = CreateConfiguration();
        var service = new TokenService(context, config);
        var beforeCreate = DateTime.UtcNow;

        // Act
        var token = await service.CreateTokenAsync(1, 1, TokenAction.Cancel, expirationDays: 1);

        // Assert
        Assert.True(token.ExpiresAt > beforeCreate.AddHours(23));
        Assert.True(token.ExpiresAt < beforeCreate.AddDays(2));
    }

    [Fact]
    public async Task CreateTokenAsync_PersistsToDatabase()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var config = CreateConfiguration();
        var service = new TokenService(context, config);

        // Act
        var token = await service.CreateTokenAsync(1, 1, TokenAction.Decline);

        // Assert
        var savedToken = await context.ActionTokens.FindAsync(token.Id);
        Assert.NotNull(savedToken);
        Assert.Equal(token.Token, savedToken.Token);
        Assert.Equal(TokenAction.Decline, savedToken.Action);
    }

    [Fact]
    public void GenerateActionUrl_ReturnsCorrectFormat()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var config = CreateConfiguration("https://shifts.example.org");
        var service = new TokenService(context, config);

        // Act
        var url = service.GenerateActionUrl("abc123");

        // Assert
        Assert.Equal("https://shifts.example.org/action/abc123", url);
    }

    [Fact]
    public void GenerateActionUrl_TrimsTrailingSlash()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var config = CreateConfiguration("https://shifts.example.org/");
        var service = new TokenService(context, config);

        // Act
        var url = service.GenerateActionUrl("xyz789");

        // Assert
        Assert.Equal("https://shifts.example.org/action/xyz789", url);
    }
}
