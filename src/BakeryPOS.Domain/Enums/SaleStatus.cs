namespace BakeryPOS.Domain.Enums;

public enum SaleStatus
{
    Pending = 0,
    Completed = 1,
    Voided = 2,
    SyncPending = 3
}

public enum CashShiftStatus
{
    Open = 0,
    Closed = 1
}

public enum CashMovementType
{
    Opening = 0,
    Income = 1,
    Expense = 2,
    Closing = 3
}

public enum OutboxEventStatus
{
    Pending = 0,
    Processing = 1,
    Processed = 2,
    Failed = 3
}

public enum SyncOrigin
{
    Server = 0,
    Device = 1
}
