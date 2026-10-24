using BakeryPOS.Application.Sales.Commands;
using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Sync.Commands;

/// <summary>
/// CU-11: Sincronizar en lote las ventas generadas sin conexión.
/// Cada venta viaja con su propio SaleId (UUID v7) generado en el dispositivo,
/// por lo que reenviarla nunca la duplica (ver CreateSaleHandler).
/// </summary>
public sealed record SyncSalesCommand(IReadOnlyList<CreateSaleCommand> Sales) : IRequest<Result<SyncSalesResponse>>;

public sealed record SyncSalesResponse(IReadOnlyList<SyncSaleResultDto> Results);

public sealed record SyncSaleResultDto(
    Guid SaleId,
    bool Accepted,
    string? ErrorCode,
    string? ErrorMessage);
