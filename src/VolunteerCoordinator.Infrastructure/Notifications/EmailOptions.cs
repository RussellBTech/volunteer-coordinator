namespace VolunteerCoordinator.Infrastructure.Notifications;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string? From { get; set; }

    public string? ReplyTo { get; set; }

    public string? PublicBaseUrl { get; set; }

    public bool IsValid(bool production)
    {
        if (!production)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(From) &&
            !string.IsNullOrWhiteSpace(ReplyTo) &&
            Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var baseUri) &&
            string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
