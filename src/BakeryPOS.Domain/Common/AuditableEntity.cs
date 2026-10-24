namespace BakeryPOS.Domain.Common;

/// <summary>
/// Base entity with audit columns matching the design schema:
/// creado_en, creado_por, modificado_en, modificado_por, version_fila, eliminado_en, eliminado_por
/// </summary>
public abstract class AuditableEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
    public int RowVersion { get; set; } = 1;
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public bool IsDeleted => DeletedAt.HasValue;

    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
