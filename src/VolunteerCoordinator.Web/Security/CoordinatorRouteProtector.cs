using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace VolunteerCoordinator.Web.Security;

public sealed class CoordinatorRouteProtector
{
    private readonly IDataProtector _protector;

    public CoordinatorRouteProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("VolunteerCoordinator.CoordinatorRoutes.v1");
    }

    public string Protect(string kind, Guid id)
    {
        var value = _protector.Protect($"{kind}|{id:N}");
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(value));
    }
    public string ProtectText(string kind, string value)
    {
        var protectedValue = _protector.Protect($"{kind}|{value}");
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(protectedValue));
    }

    public bool TryUnprotectText(
        string? value,
        string expectedKind,
        out string text)
    {
        text = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var plaintext = _protector.Unprotect(Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(value)));
            var parts = plaintext.Split('|', 2);
            if (parts.Length != 2 ||
                !string.Equals(parts[0], expectedKind, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(parts[1]))
            {
                return false;
            }

            text = parts[1];
            return true;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return false;
        }
    }


    public bool TryUnprotect(string? value, out string kind, out Guid id)
    {
        kind = string.Empty;
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var plaintext = _protector.Unprotect(Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(value)));
            var parts = plaintext.Split('|', 2);
            return parts.Length == 2 &&
                   !string.IsNullOrWhiteSpace(parts[0]) &&
                   Guid.TryParseExact(parts[1], "N", out id) &&
                   (kind = parts[0]) is not null;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return false;
        }
    }
}
