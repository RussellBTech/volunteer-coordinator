using System.Net.Mail;

namespace VolunteerCoordinator.Web.Privacy;

public sealed class PrivacyOptions
{
    public const string SectionName = "Privacy";

    public string ContactEmail { get; set; } = "privacy@example.invalid";

    public bool IsValid(string environmentName)
    {
        if (string.IsNullOrWhiteSpace(ContactEmail))
        {
            return false;
        }

        try
        {
            var address = new MailAddress(ContactEmail.Trim());
            if (!string.Equals(address.Address, ContactEmail.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        catch (FormatException)
        {
            return false;
        }

        return !string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase) ||
            !ContactEmail.Trim().EndsWith(".invalid", StringComparison.OrdinalIgnoreCase);
    }
}
