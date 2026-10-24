using MediatR;

namespace BakeryPOS.Domain.Common;

public interface IDomainEvent : INotification
{
    Guid EventId { get; }
    DateTime OccurredOn { get; }
}

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.CreateVersion7();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
