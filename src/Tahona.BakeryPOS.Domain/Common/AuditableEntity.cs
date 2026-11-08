namespace Tahona.BakeryPOS.Domain.Common;

/// <summary>
/// Base de las entidades persistentes: id UUID v7 generado por la aplicación, columnas de auditoría,
/// concurrencia optimista (rowversion de SQL Server) y borrado lógico para datos maestros.
/// Las columnas de auditoría las llena el DbContext; el dominio no las toca.
/// </summary>
public abstract class AuditableEntity
{
    public Guid Id { get; protected init; } = Guid.CreateVersion7();

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public bool IsDeleted => DeletedAt.HasValue;

    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
