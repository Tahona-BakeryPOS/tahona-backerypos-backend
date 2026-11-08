using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Domain.Cash;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Cash.Sessions;

/// <param name="Status"><c>open</c>, <c>closing</c> o <c>closed</c>.</param>
public sealed record CashSessionDto(Guid Id, Guid BranchId, string BranchName, Guid CashRegisterId, string CashRegisterName, string Status,
    DateTime OpenedAt, Guid OpenedBy, string? OpenedByName, decimal OpeningAmount, DateTime? ClosedAt, string? ClosedByName,
    decimal? ExpectedAmount, decimal? CountedAmount, decimal? Difference, string? Notes);

/// <param name="Kind"><c>opening</c>, <c>sale_cash</c>, <c>change_given</c>, <c>refund_cash</c>, <c>income</c> o <c>expense</c>.</param>
/// <param name="SignedAmount">Con signo para el esperado.</param>
public sealed record CashMovementDto(Guid Id, DateTime OccurredAt, string Kind, decimal Amount, decimal SignedAmount, string? ReasonName,
    string? UserName, string? AuthorizedByName, string? Note);

/// <summary>Desglose del esperado (HU-5.4) y pagos que bloquean el cierre (D-12).</summary>
public sealed record CashSessionSummaryDto(Guid SessionId, decimal OpeningAmount, decimal CashSales, decimal ChangeGiven, decimal CashRefunds,
    decimal Incomes, decimal Expenses, decimal Expected, int PendingPayments);

/// <param name="Kind"><c>income</c> o <c>expense</c>.</param>
public sealed record CashReasonDto(Guid Id, string Kind, string Name, bool IsActive);

/// <summary>Abre la sesión de la caja (HU-5.2): una sola abierta por caja; efectivo inicial ≥ 0.</summary>
public sealed record OpenCashSessionCommand(Guid CashRegisterId, decimal OpeningAmount) : IRequest<Result<CashSessionDto>>;

/// <summary>Sesión abierta de la caja; 404 si no hay (relevo de cajero o equipo reinstalado).</summary>
public sealed record GetOpenCashSessionQuery(Guid CashRegisterId) : IRequest<Result<CashSessionDto>>;

/// <summary>
/// Ingreso o retiro de efectivo con motivo (HU-5.3). El retiro exige <c>caja.retiro</c> (el superior que
/// autoriza envía su propio token) y no puede superar el esperado.
/// </summary>
public sealed record RegisterCashMovementCommand(Guid SessionId, string Kind, decimal Amount, Guid ReasonId, string? Note)
    : IRequest<Result<CashMovementDto>>;

public sealed record GetCashMovementsQuery(Guid SessionId) : IRequest<Result<IReadOnlyList<CashMovementDto>>>;

public sealed record GetCashSessionSummaryQuery(Guid SessionId) : IRequest<Result<CashSessionSummaryDto>>;

/// <summary>Cierra con el efectivo contado (HU-5.4). Bloqueado si hay pagos pendientes (D-12).</summary>
public sealed record CloseCashSessionCommand(Guid SessionId, decimal CountedAmount, string? Notes) : IRequest<Result<CashSessionDto>>;

/// <summary>Sesiones por fecha de apertura (en UTC), sucursal y estado, de la más reciente a la más antigua.</summary>
public sealed record GetCashSessionsQuery(DateTime? From, DateTime? To, Guid? BranchId, string? Status) : IRequest<Result<IReadOnlyList<CashSessionDto>>>;

public sealed record GetCashReasonsQuery : IRequest<Result<IReadOnlyList<CashReasonDto>>>;

public sealed class OpenCashSessionValidator : AbstractValidator<OpenCashSessionCommand>
{
    public OpenCashSessionValidator()
    {
        RuleFor(x => x.OpeningAmount).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, true);
    }
}

public sealed class RegisterCashMovementValidator : AbstractValidator<RegisterCashMovementCommand>
{
    public RegisterCashMovementValidator()
    {
        RuleFor(x => x.Kind).Must(k => k is "income" or "expense").WithMessage("Tipo: income (ingreso) o expense (retiro).");
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.ReasonId).NotEmpty().WithMessage("El motivo es obligatorio.");
        RuleFor(x => x.Note).MaximumLength(200);
    }
}

public sealed class CloseCashSessionValidator : AbstractValidator<CloseCashSessionCommand>
{
    public CloseCashSessionValidator()
    {
        RuleFor(x => x.CountedAmount).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

public sealed class GetCashSessionsValidator : AbstractValidator<GetCashSessionsQuery>
{
    public GetCashSessionsValidator()
    {
        RuleFor(x => x.Status).Must(s => s is null or "open" or "closed").WithMessage("Estado: open o closed.");
    }
}

public sealed class CashSessionsHandler :
    IRequestHandler<OpenCashSessionCommand, Result<CashSessionDto>>,
    IRequestHandler<GetOpenCashSessionQuery, Result<CashSessionDto>>,
    IRequestHandler<RegisterCashMovementCommand, Result<CashMovementDto>>,
    IRequestHandler<GetCashMovementsQuery, Result<IReadOnlyList<CashMovementDto>>>,
    IRequestHandler<GetCashSessionSummaryQuery, Result<CashSessionSummaryDto>>,
    IRequestHandler<CloseCashSessionCommand, Result<CashSessionDto>>,
    IRequestHandler<GetCashSessionsQuery, Result<IReadOnlyList<CashSessionDto>>>,
    IRequestHandler<GetCashReasonsQuery, Result<IReadOnlyList<CashReasonDto>>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IBranchDirectory _branches;
    private readonly IUserDirectory _users;
    private readonly IPendingPaymentsProbe _pending;
    private readonly TimeProvider _clock;

    public CashSessionsHandler(IAppDbContext db, ITenantContext tenant, IBranchDirectory branches, IUserDirectory users,
        IPendingPaymentsProbe pending, TimeProvider clock)
    {
        _db = db;
        _tenant = tenant;
        _branches = branches;
        _users = users;
        _pending = pending;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<Result<CashSessionDto>> Handle(OpenCashSessionCommand request, CancellationToken ct)
    {
        var register = await _db.CashRegisters.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.CashRegisterId, ct);
        if (register is null)
            return Result.Failure<CashSessionDto>(Error.NotFound("Caja", request.CashRegisterId));
        if (!_tenant.CanAccessBranch(register.BranchId))
            return Result.Failure<CashSessionDto>(Error.SinAsignacionVigente);
        if (!register.IsActive)
            return Result.Failure<CashSessionDto>(Error.Validation("La caja está desactivada."));
        // El índice único filtrado resuelve la carrera entre dos equipos que abren a la vez.
        if (await _db.CashSessions.AnyAsync(s => s.CashRegisterId == register.Id && s.Status != CashSessionStatus.Closed, ct))
            return Result.Failure<CashSessionDto>(Error.CajaYaAbierta);

        var userId = _tenant.UserId ?? throw new InvalidOperationException("Abrir caja requiere un usuario en sesión.");
        var session = CashSession.Open(register.CompanyId, register.BranchId, register.Id, userId, request.OpeningAmount, Now);
        _db.CashSessions.Add(session);
        if (request.OpeningAmount > 0)
            _db.CashMovements.Add(CashMovement.Create(session, CashMovementKind.Opening, request.OpeningAmount, Now, userId));
        await _db.SaveChangesAsync(ct);
        return Result.Success((await ToDtosAsync([session], ct))[0]);
    }

    public async Task<Result<CashSessionDto>> Handle(GetOpenCashSessionQuery request, CancellationToken ct)
    {
        var session = await _db.CashSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CashRegisterId == request.CashRegisterId && s.Status != CashSessionStatus.Closed, ct);
        if (session is null || !_tenant.CanAccessBranch(session.BranchId))
            return Result.Failure<CashSessionDto>(Error.NoEncontrado with { Message = "La caja no tiene una sesión abierta." });
        return Result.Success((await ToDtosAsync([session], ct))[0]);
    }

    public async Task<Result<CashMovementDto>> Handle(RegisterCashMovementCommand request, CancellationToken ct)
    {
        var session = await FindSessionAsync(request.SessionId, tracked: true, ct);
        if (session.IsFailure)
            return Result.Failure<CashMovementDto>(session.Error);
        if (!session.Value.IsOpen)
            return Result.Failure<CashMovementDto>(Error.CajaCerrada);

        var kind = request.Kind == "expense" ? CashMovementKind.Expense : CashMovementKind.Income;
        if (kind == CashMovementKind.Expense && !_tenant.HasPermission(Permissions.CajaRetiro))
            return Result.Failure<CashMovementDto>(Error.SinPermiso with { Message = "Los retiros los autoriza un superior (permiso caja.retiro)." });

        var reason = await _db.CashReasons.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.ReasonId, ct);
        if (reason is null)
            return Result.Failure<CashMovementDto>(Error.NotFound("Motivo", request.ReasonId));
        if (!reason.IsActive || reason.Kind != (kind == CashMovementKind.Expense ? CashReasonKind.Expense : CashReasonKind.Income))
            return Result.Failure<CashMovementDto>(Error.Validation("El motivo no corresponde al tipo de movimiento o está desactivado."));

        if (kind == CashMovementKind.Expense)
        {
            var expected = CashMovement.Expected(await MovementsOfAsync(session.Value.Id, ct));
            if (request.Amount > expected)
                return Result.Failure<CashMovementDto>(Error.RetiroExcedeSaldo with { Message = $"El retiro supera el efectivo esperado ({expected:N0})." });
        }

        var movement = CashMovement.Create(session.Value, kind, request.Amount, Now, _tenant.UserId, reason.Id,
            kind == CashMovementKind.Expense ? _tenant.UserId : null, request.Note);
        _db.CashMovements.Add(movement);
        await _db.SaveChangesAsync(ct);
        return Result.Success((await ToMovementDtosAsync([movement], ct))[0]);
    }

    public async Task<Result<IReadOnlyList<CashMovementDto>>> Handle(GetCashMovementsQuery request, CancellationToken ct)
    {
        var session = await FindSessionAsync(request.SessionId, tracked: false, ct);
        if (session.IsFailure)
            return Result.Failure<IReadOnlyList<CashMovementDto>>(session.Error);
        return Result.Success(await ToMovementDtosAsync(await MovementsOfAsync(request.SessionId, ct), ct));
    }

    public async Task<Result<CashSessionSummaryDto>> Handle(GetCashSessionSummaryQuery request, CancellationToken ct)
    {
        var session = await FindSessionAsync(request.SessionId, tracked: false, ct);
        if (session.IsFailure)
            return Result.Failure<CashSessionSummaryDto>(session.Error);
        var movements = await MovementsOfAsync(request.SessionId, ct);
        decimal Sum(CashMovementKind k) => movements.Where(m => m.Kind == k).Sum(m => m.Amount);
        var pending = await _pending.CountForCashSessionAsync(request.SessionId, ct);
        return Result.Success(new CashSessionSummaryDto(request.SessionId, session.Value.OpeningAmount, Sum(CashMovementKind.SaleCash),
            Sum(CashMovementKind.ChangeGiven), Sum(CashMovementKind.RefundCash), Sum(CashMovementKind.Income), Sum(CashMovementKind.Expense),
            CashMovement.Expected(movements), pending));
    }

    public async Task<Result<CashSessionDto>> Handle(CloseCashSessionCommand request, CancellationToken ct)
    {
        var session = await FindSessionAsync(request.SessionId, tracked: true, ct);
        if (session.IsFailure)
            return Result.Failure<CashSessionDto>(session.Error);
        if (!session.Value.IsOpen)
            return Result.Failure<CashSessionDto>(Error.CajaCerrada);

        var pending = await _pending.CountForCashSessionAsync(request.SessionId, ct);
        if (pending > 0)
            return Result.Failure<CashSessionDto>(Error.PagoPendiente(pending));

        var expected = CashMovement.Expected(await MovementsOfAsync(request.SessionId, ct));
        var userId = _tenant.UserId ?? throw new InvalidOperationException("Cerrar caja requiere un usuario en sesión.");
        session.Value.Close(expected, request.CountedAmount, userId, Now, request.Notes);
        await _db.SaveChangesAsync(ct);
        return Result.Success((await ToDtosAsync([session.Value], ct))[0]);
    }

    public async Task<Result<IReadOnlyList<CashSessionDto>>> Handle(GetCashSessionsQuery request, CancellationToken ct)
    {
        var query = _db.CashSessions.AsNoTracking();
        if (request.BranchId is { } branchId) query = query.Where(s => s.BranchId == branchId);
        if (request.From is { } from) query = query.Where(s => s.OpenedAt >= from.ToUniversalTime());
        if (request.To is { } to) query = query.Where(s => s.OpenedAt <= to.ToUniversalTime());
        if (request.Status == "open") query = query.Where(s => s.Status != CashSessionStatus.Closed);
        if (request.Status == "closed") query = query.Where(s => s.Status == CashSessionStatus.Closed);

        var sessions = (await query.OrderByDescending(s => s.OpenedAt).Take(500).ToListAsync(ct))
            .Where(s => _tenant.CanAccessBranch(s.BranchId)).ToList();
        return Result.Success(await ToDtosAsync(sessions, ct));
    }

    public async Task<Result<IReadOnlyList<CashReasonDto>>> Handle(GetCashReasonsQuery request, CancellationToken ct)
    {
        var reasons = await _db.CashReasons.AsNoTracking().OrderBy(r => r.Kind).ThenBy(r => r.Name).ToListAsync(ct);
        IReadOnlyList<CashReasonDto> list = reasons
            .Select(r => new CashReasonDto(r.Id, r.Kind.ToString().ToLowerInvariant(), r.Name, r.IsActive)).ToList();
        return Result.Success(list);
    }

    /// <summary>Sesión de la empresa y dentro del alcance por sucursal; 404 si no.</summary>
    private async Task<Result<CashSession>> FindSessionAsync(Guid id, bool tracked, CancellationToken ct)
    {
        var source = tracked ? _db.CashSessions : _db.CashSessions.AsNoTracking();
        var session = await source.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null)
            return Result.Failure<CashSession>(Error.NotFound("Sesión de caja", id));
        return _tenant.CanAccessBranch(session.BranchId) ? Result.Success(session) : Result.Failure<CashSession>(Error.SinAsignacionVigente);
    }

    private async Task<List<CashMovement>> MovementsOfAsync(Guid sessionId, CancellationToken ct) =>
        await _db.CashMovements.AsNoTracking().Where(m => m.CashSessionId == sessionId).OrderBy(m => m.OccurredAt).ThenBy(m => m.Id).ToListAsync(ct);

    private async Task<IReadOnlyList<CashSessionDto>> ToDtosAsync(IReadOnlyList<CashSession> sessions, CancellationToken ct)
    {
        var registerIds = sessions.Select(s => s.CashRegisterId).Distinct().ToList();
        var registers = await _db.CashRegisters.AsNoTracking().Where(r => registerIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var branches = new Dictionary<Guid, string>();
        foreach (var branchId in sessions.Select(s => s.BranchId).Distinct())
            branches[branchId] = (await _branches.FindAsync(branchId, ct))?.Name ?? "";
        var users = await _users.NamesAsync(sessions.SelectMany(s => new[] { (Guid?)s.OpenedBy, s.ClosedBy }).OfType<Guid>().ToList(), ct);

        return sessions.Select(s => new CashSessionDto(s.Id, s.BranchId, branches[s.BranchId], s.CashRegisterId,
            registers.GetValueOrDefault(s.CashRegisterId, ""), s.Status.ToString().ToLowerInvariant(), s.OpenedAt, s.OpenedBy,
            users.GetValueOrDefault(s.OpenedBy), s.OpeningAmount, s.ClosedAt, s.ClosedBy is { } c ? users.GetValueOrDefault(c) : null,
            s.ExpectedAmount, s.CountedAmount, s.Difference, s.Notes)).ToList();
    }

    private async Task<IReadOnlyList<CashMovementDto>> ToMovementDtosAsync(IReadOnlyList<CashMovement> movements, CancellationToken ct)
    {
        var reasonIds = movements.Where(m => m.ReasonId != null).Select(m => m.ReasonId!.Value).Distinct().ToList();
        var reasons = await _db.CashReasons.AsNoTracking().Where(r => reasonIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var users = await _users.NamesAsync(movements.SelectMany(m => new[] { m.UserId, m.AuthorizedBy }).OfType<Guid>().ToList(), ct);
        return movements.Select(m => new CashMovementDto(m.Id, m.OccurredAt, KindName(m.Kind), m.Amount, m.SignedAmount,
            m.ReasonId is { } r ? reasons.GetValueOrDefault(r) : null,
            m.UserId is { } u ? users.GetValueOrDefault(u) : null,
            m.AuthorizedBy is { } a ? users.GetValueOrDefault(a) : null, m.Note)).ToList();
    }

    private static string KindName(CashMovementKind kind) =>
        string.Concat(kind.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}

/// <summary>Motivos de caja por defecto de cada empresa (I-22).</summary>
public sealed class CashProvisioning : ICashProvisioning
{
    private readonly IAppDbContext _db;

    public CashProvisioning(IAppDbContext db)
    {
        _db = db;
    }

    public void ProvisionCompany(Guid companyId)
    {
        foreach (var (kind, name) in DefaultCashReasons.All)
            _db.CashReasons.Add(CashReason.Create(companyId, kind, name));
    }
}

/// <summary>El efectivo de una venta entra a la gaveta: recibido (sale_cash) y cambio entregado (change_given).</summary>
public sealed class CashDrawer : ICashDrawer
{
    public const string SaleSource = "sale";

    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;

    public CashDrawer(IAppDbContext db, ITenantContext tenant, TimeProvider clock)
    {
        _db = db;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task RecordSaleCashAsync(Guid cashSessionId, Guid saleId, decimal received, decimal change, CancellationToken ct)
    {
        if (received <= 0)
            return;
        // Idempotente: una venta deja su efectivo una sola vez (índice único por tipo y origen).
        if (await _db.CashMovements.AnyAsync(m => m.SourceType == SaleSource && m.SourceId == saleId, ct))
            return;
        var session = await _db.CashSessions.FirstAsync(s => s.Id == cashSessionId, ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        _db.CashMovements.Add(CashMovement.Create(session, CashMovementKind.SaleCash, received, now, _tenant.UserId, sourceType: SaleSource, sourceId: saleId));
        if (change > 0)
            _db.CashMovements.Add(CashMovement.Create(session, CashMovementKind.ChangeGiven, change, now, _tenant.UserId, sourceType: SaleSource, sourceId: saleId));
    }

    public async Task<Result> RecordRefundAsync(Guid cashSessionId, string sourceType, Guid sourceId, decimal amount, Guid authorizedBy,
        CancellationToken ct)
    {
        if (await _db.CashMovements.AnyAsync(m => m.Kind == CashMovementKind.RefundCash && m.SourceType == sourceType && m.SourceId == sourceId, ct))
            return Result.Success();
        var session = await _db.CashSessions.FirstOrDefaultAsync(s => s.Id == cashSessionId, ct);
        if (session is null || !session.IsOpen)
            return Result.Failure(Error.CajaCerrada);
        var movements = await _db.CashMovements.AsNoTracking().Where(m => m.CashSessionId == cashSessionId).ToListAsync(ct);
        var expected = CashMovement.Expected(movements);
        if (amount > expected)
            return Result.Failure(Error.RetiroExcedeSaldo with { Message = $"No hay efectivo suficiente en la caja para devolver {amount:N0} (esperado {expected:N0})." });
        _db.CashMovements.Add(CashMovement.Create(session, CashMovementKind.RefundCash, amount, _clock.GetUtcNow().UtcDateTime, _tenant.UserId,
            authorizedBy: authorizedBy, note: sourceType == RefundSources.Void ? "Anulación de venta" : "Devolución de venta",
            sourceType: sourceType, sourceId: sourceId));
        return Result.Success();
    }
}

/// <summary>Lo que otros módulos pueden consultar de las sesiones de caja.</summary>
public sealed class CashSessionDirectory : ICashSessionDirectory
{
    private readonly IAppDbContext _db;

    public CashSessionDirectory(IAppDbContext db)
    {
        _db = db;
    }

    public Task<CashSessionSummary?> FindAsync(Guid cashSessionId, CancellationToken ct) =>
        _db.CashSessions.AsNoTracking().Where(s => s.Id == cashSessionId)
            .Select(s => new CashSessionSummary(s.Id, s.BranchId, s.CashRegisterId, s.Status != CashSessionStatus.Closed))
            .FirstOrDefaultAsync(ct);

    public Task<CashSessionSummary?> FindOpenForRegisterAsync(Guid cashRegisterId, CancellationToken ct) =>
        _db.CashSessions.AsNoTracking().Where(s => s.CashRegisterId == cashRegisterId && s.Status != CashSessionStatus.Closed)
            .Select(s => new CashSessionSummary(s.Id, s.BranchId, s.CashRegisterId, true))
            .FirstOrDefaultAsync(ct);
}
