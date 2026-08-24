namespace VolunteerCoordinator.Domain.Volunteers;

public sealed class Volunteer
{
    private Volunteer()
    {
    }

    private Volunteer(string name, string email, string? phone, DateTimeOffset nowUtc)
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = nowUtc;
        UpdateContact(name, email, phone, nowUtc);
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Volunteer Create(string name, string email, string? phone, DateTimeOffset nowUtc) =>
        new(name, email, phone, nowUtc);

    public void UpdateContact(string name, string email, string? phone, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
        {
            throw new DomainException("Volunteer name is required and cannot exceed 120 characters.");
        }

        var normalizedEmail = NormalizeEmail(email);
        if (email.Trim().Length > 320 || !normalizedEmail.Contains('@', StringComparison.Ordinal))
        {
            throw new DomainException("A valid volunteer email is required.");
        }

        if (phone?.Trim().Length > 40)
        {
            throw new DomainException("Phone cannot exceed 40 characters.");
        }

        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Volunteer timestamps must be UTC.");
        }

        Name = name.Trim();
        Email = email.Trim();
        NormalizedEmail = normalizedEmail;
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        UpdatedAtUtc = nowUtc;
    }

    public static string NormalizeEmail(string email) =>
        string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToUpperInvariant();
}
