using Microsoft.Extensions.Configuration;
using VSMS.Core.Entities;
using VSMS.Core.Enums;
using VSMS.Core.Interfaces;
using VSMS.Infrastructure.Data;

namespace VSMS.Infrastructure.Services;

public class TokenService : ITokenService
{
    private readonly VsmsDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public TokenService(VsmsDbContext dbContext, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _configuration = configuration;
    }

    public async Task<ActionToken> CreateTokenAsync(int shiftId, int volunteerId, TokenAction action, int? expirationDays = null)
    {
        var configuredDays = _configuration["App:TokenExpirationDays"];
        var days = expirationDays ?? (int.TryParse(configuredDays, out var d) ? d : 14);

        var token = new ActionToken
        {
            Token = Guid.NewGuid().ToString("N"),
            ShiftId = shiftId,
            VolunteerId = volunteerId,
            Action = action,
            ExpiresAt = DateTime.UtcNow.AddDays(days),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ActionTokens.Add(token);
        await _dbContext.SaveChangesAsync();

        return token;
    }

    public string GenerateActionUrl(ActionToken token)
    {
        return GenerateActionUrl(token.Token);
    }

    public string GenerateActionUrl(string tokenValue)
    {
        var baseUrl = _configuration["App:BaseUrl"]?.TrimEnd('/') ?? "https://localhost:5001";
        return $"{baseUrl}/action/{tokenValue}";
    }
}
