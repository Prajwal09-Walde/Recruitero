namespace RecruitAI.Domain.Common;

/// <summary>Base class for all domain entities. Includes audit fields and domain event dispatch.</summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; protected set; }

    public void SetId(Guid id) => Id = id;

    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) =>
        _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void MarkUpdated() => UpdatedAt = DateTime.UtcNow;
}

public interface IDomainEvent : MediatR.INotification
{
    DateTime OccurredOn { get; }
}
