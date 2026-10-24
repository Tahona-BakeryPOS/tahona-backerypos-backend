using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Events;

public sealed record SaleCompletedEvent(
    Guid SaleId,
    Guid OrganizationId,
    Guid BranchId,
    decimal Total) : DomainEvent;

public sealed record SaleVoidedEvent(
    Guid SaleId,
    Guid OrganizationId,
    string Reason) : DomainEvent;

public sealed record SaleSyncedEvent(
    Guid SaleId,
    Guid DeviceId) : DomainEvent;
