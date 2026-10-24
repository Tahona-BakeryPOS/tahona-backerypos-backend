using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: unit_of_measure / unidad_medida
/// kg, g, lb, unidad, docena, etc.
/// </summary>
public class UnitOfMeasure : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
