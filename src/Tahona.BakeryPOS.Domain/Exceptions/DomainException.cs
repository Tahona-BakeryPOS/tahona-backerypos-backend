namespace Tahona.BakeryPOS.Domain.Exceptions;

public class DomainException : Exception
{
    public string ErrorCode { get; }

    public DomainException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}

public class NotFoundException : DomainException
{
    public NotFoundException(string entity, Guid id)
        : base("NOT_FOUND", $"{entity} con ID '{id}' no fue encontrado.")
    { }
}

public class ConflictException : DomainException
{
    public ConflictException(string message)
        : base("VERSION_CONFLICT", message)
    { }
}

public class BusinessRuleException : DomainException
{
    public BusinessRuleException(string errorCode, string message)
        : base(errorCode, message)
    { }
}
