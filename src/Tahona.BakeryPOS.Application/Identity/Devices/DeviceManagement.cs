using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Domain.Identity;
using Tahona.BakeryPOS.SharedKernel.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Identity.Devices;

/// <summary>Genera un código de un solo uso (15 min) para vincular un equipo a la caja.</summary>
public sealed record CreatePairingCodeCommand(Guid CashRegisterId) : IRequest<Result<PairingCodeDto>>;

/// <param name="Code">Solo se muestra en esta respuesta; la base guarda su hash.</param>
public sealed record PairingCodeDto(string Code, DateTime ExpiresAt, Guid CashRegisterId);

public sealed record GetDevicesQuery : IRequest<Result<IReadOnlyList<DeviceDto>>>;

/// <summary>Desactivar un equipo revoca su token: no puede volver a entrar sin vincularse de nuevo.</summary>
public sealed record DeactivateDeviceCommand(Guid Id) : IRequest<Result<DeviceDto>>;

public sealed record DeviceDto(Guid Id, Guid CashRegisterId, string Name, bool IsActive, DateTime? LastSeenAt, DateTime CreatedAt);

public sealed class DeviceManagementHandler :
    IRequestHandler<CreatePairingCodeCommand, Result<PairingCodeDto>>,
    IRequestHandler<GetDevicesQuery, Result<IReadOnlyList<DeviceDto>>>,
    IRequestHandler<DeactivateDeviceCommand, Result<DeviceDto>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICashRegisterDirectory _registers;
    private readonly TimeProvider _clock;

    public DeviceManagementHandler(IAppDbContext db, ITenantContext tenant, ICashRegisterDirectory registers, TimeProvider clock)
    {
        _db = db;
        _tenant = tenant;
        _registers = registers;
        _clock = clock;
    }

    public async Task<Result<PairingCodeDto>> Handle(CreatePairingCodeCommand request, CancellationToken ct)
    {
        var register = await _registers.FindAsync(request.CashRegisterId, ct);
        if (register is null)
            return Result.Failure<PairingCodeDto>(Error.NotFound("Caja", request.CashRegisterId));
        if (!register.IsActive)
            return Result.Failure<PairingCodeDto>(Error.Validation("La caja está desactivada."));

        var code = SecretTokens.NewPairingCode();
        var pairing = DevicePairingCode.Create(_tenant.RequiredCompanyId, register.Id, SecretTokens.Hash(code), _clock.GetUtcNow().UtcDateTime);
        _db.DevicePairingCodes.Add(pairing);
        await _db.SaveChangesAsync(ct);
        return Result.Success(new PairingCodeDto(code, pairing.ExpiresAt, register.Id));
    }

    public async Task<Result<IReadOnlyList<DeviceDto>>> Handle(GetDevicesQuery request, CancellationToken ct)
    {
        IReadOnlyList<DeviceDto> devices = await _db.Devices.AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new DeviceDto(d.Id, d.CashRegisterId, d.Name, d.IsActive, d.LastSeenAt, d.CreatedAt))
            .ToListAsync(ct);
        return Result.Success(devices);
    }

    public async Task<Result<DeviceDto>> Handle(DeactivateDeviceCommand request, CancellationToken ct)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == request.Id, ct);
        if (device is null)
            return Result.Failure<DeviceDto>(Error.NotFound("Dispositivo", request.Id));

        device.Deactivate();
        await _db.SaveChangesAsync(ct);
        return Result.Success(new DeviceDto(device.Id, device.CashRegisterId, device.Name, device.IsActive, device.LastSeenAt, device.CreatedAt));
    }
}
