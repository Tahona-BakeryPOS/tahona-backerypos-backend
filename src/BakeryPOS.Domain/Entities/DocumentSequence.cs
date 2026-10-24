using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: document_sequence / secuencia_documental
/// Auto-incrementing document numbers per branch (POS-001-00001).
/// </summary>
public class DocumentSequence : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public Guid BranchId { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public long CurrentNumber { get; set; }
    public int PaddingLength { get; set; } = 5;
    public bool IsActive { get; set; } = true;

    // Navigation
    public Branch Branch { get; set; } = null!;

    public string GetNextNumber()
    {
        CurrentNumber++;
        return $"{Prefix}-{CurrentNumber.ToString().PadLeft(PaddingLength, '0')}";
    }
}
