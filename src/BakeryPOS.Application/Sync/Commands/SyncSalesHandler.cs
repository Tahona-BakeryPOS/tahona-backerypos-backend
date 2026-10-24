using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Sync.Commands;

/// <summary>
/// Procesa el lote en orden cronológico (el orden en que llega la lista),
/// aislando cada venta: el rechazo de una no detiene las demás, y el motivo
/// de rechazo queda disponible para que el propietario lo vea en el backoffice.
/// </summary>
public sealed class SyncSalesHandler : IRequestHandler<SyncSalesCommand, Result<SyncSalesResponse>>
{
    private readonly IMediator _mediator;

    public SyncSalesHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<Result<SyncSalesResponse>> Handle(SyncSalesCommand request, CancellationToken ct)
    {
        var results = new List<SyncSaleResultDto>(request.Sales.Count);

        foreach (var saleCommand in request.Sales)
        {
            try
            {
                var result = await _mediator.Send(saleCommand, ct);

                results.Add(result.IsSuccess
                    ? new SyncSaleResultDto(saleCommand.SaleId, true, null, null)
                    : new SyncSaleResultDto(saleCommand.SaleId, false, result.Error.Code, result.Error.Message));
            }
            catch (Exception ex)
            {
                // Una venta con datos corruptos no debe tumbar el resto del lote.
                results.Add(new SyncSaleResultDto(
                    saleCommand.SaleId, false, "VENTA_RECHAZADA_SYNC", ex.Message));
            }
        }

        return Result.Success(new SyncSalesResponse(results));
    }
}
