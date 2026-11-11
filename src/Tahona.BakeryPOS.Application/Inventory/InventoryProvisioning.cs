using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Inventory;

namespace Tahona.BakeryPOS.Application.Inventory;

/// <summary>Default adjustment reasons of each company and the "Mostrador" location of each branch (I-21).</summary>
public sealed class InventoryProvisioning : IInventoryProvisioning
{
    private readonly IAppDbContext _db;

    public InventoryProvisioning(IAppDbContext db)
    {
        _db = db;
    }

    public void ProvisionCompany(Guid companyId)
    {
        foreach (var (code, name, direction) in DefaultAdjustmentReasons.All)
            _db.AdjustmentReasons.Add(AdjustmentReason.Create(companyId, code, name, direction));
    }

    public void ProvisionBranch(Guid companyId, Guid branchId) =>
        _db.StockLocations.Add(StockLocation.CreateDefault(companyId, branchId));
}
