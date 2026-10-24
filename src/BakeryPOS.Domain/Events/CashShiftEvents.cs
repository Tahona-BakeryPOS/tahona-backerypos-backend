using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Events;

public sealed record CashShiftOpenedEvent(
    Guid CashShiftId,
    Guid CashRegisterId,
    Guid UserId) : DomainEvent;

public sealed record CashShiftClosedEvent(
    Guid CashShiftId,
    Guid CashRegisterId,
    Guid UserId) : DomainEvent;
