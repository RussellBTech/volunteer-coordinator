namespace VolunteerCoordinator.Domain.Auditing;

public sealed class AuditEntry
{
    private AuditEntry()
    {
    }

    private AuditEntry(
        DateTimeOffset occurredAtUtc,
        string actor,
        string action,
        string entityKind,
        Guid entityId,
        string detailJson)
    {
        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Audit timestamps must be UTC.");
        }

        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(entityKind))
        {
            throw new DomainException("Audit actor, action, and entity kind are required.");
        }

        Id = Guid.NewGuid();
        OccurredAtUtc = occurredAtUtc;
        Actor = actor.Trim();
        Action = action.Trim();
        EntityKind = entityKind.Trim();
        EntityId = entityId;
        DetailJson = string.IsNullOrWhiteSpace(detailJson) ? "{}" : detailJson;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public string Actor { get; private set; } = string.Empty;

    public string Action { get; private set; } = string.Empty;

    public string EntityKind { get; private set; } = string.Empty;

    public Guid EntityId { get; private set; }

    public string DetailJson { get; private set; } = "{}";

    public static AuditEntry Create(
        DateTimeOffset occurredAtUtc,
        string actor,
        string action,
        string entityKind,
        Guid entityId,
        string detailJson) =>
        new(occurredAtUtc, actor, action, entityKind, entityId, detailJson);
}
