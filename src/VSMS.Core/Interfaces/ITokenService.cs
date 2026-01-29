using VSMS.Core.Entities;
using VSMS.Core.Enums;

namespace VSMS.Core.Interfaces;

public interface ITokenService
{
    Task<ActionToken> CreateTokenAsync(int shiftId, int volunteerId, TokenAction action, int? expirationDays = null);
    string GenerateActionUrl(ActionToken token);
    string GenerateActionUrl(string tokenValue);
}
