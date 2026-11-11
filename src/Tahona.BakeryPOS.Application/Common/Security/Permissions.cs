namespace Tahona.BakeryPOS.Application.Common.Security;

/// <summary>
/// Catálogo global de permisos (claim "permission"). Se siembra por migración desde <see cref="All"/>:
/// un permiso nuevo se agrega aquí y requiere una migración, si no nadie podrá tenerlo.
/// No existe <c>ventas.sincronizar</c>: el sistema no tiene modo sin conexión.
/// </summary>
public static class Permissions
{
    // Plataforma (nunca se asigna a un rol de empresa)
    public const string PlataformaAdmin = "plataforma.admin";

    // Empresa
    public const string OrganizacionGestionar = "organizacion.gestionar";
    public const string ReportesVer = "reportes.ver";

    // Catálogo
    public const string ProductosVer = "productos.ver";
    public const string CatalogoGestionar = "catalogo.gestionar";
    public const string PreciosSucursal = "precios.sucursal";

    // Inventario
    public const string InventarioVer = "inventario.ver";
    public const string InventarioAjustar = "inventario.ajustar";

    // Caja
    public const string TurnosAbrir = "turnos.abrir";
    public const string TurnosCerrar = "turnos.cerrar";
    public const string CajaMovimientos = "caja.movimientos";
    public const string CajaRetiro = "caja.retiro";

    // Ventas y devoluciones
    public const string VentasCrear = "ventas.crear";
    public const string VentasVer = "ventas.ver";
    public const string VentasAnular = "ventas.anular";
    public const string VentasDescuento = "ventas.descuento";
    public const string DevolucionesCrear = "devoluciones.crear";
    public const string DevolucionesAprobar = "devoluciones.aprobar";

    // Pagos
    public const string PagosResolver = "pagos.resolver";

    // Facturación electrónica
    public const string FiscalGestionar = "fiscal.gestionar";

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(PlataformaAdmin, "Administrar la plataforma", "Plataforma"),
        new(OrganizacionGestionar, "Gestionar empresa, sucursales, cajas y usuarios", "Empresa"),
        new(ReportesVer, "Ver reportes", "Empresa"),
        new(ProductosVer, "Ver productos", "Catálogo"),
        new(CatalogoGestionar, "Gestionar catálogo y precios base", "Catálogo"),
        new(PreciosSucursal, "Gestionar precios y disponibilidad de la sucursal", "Catálogo"),
        new(InventarioVer, "Ver existencias y movimientos", "Inventario"),
        new(InventarioAjustar, "Registrar ajustes de inventario", "Inventario"),
        new(TurnosAbrir, "Abrir caja", "Caja"),
        new(TurnosCerrar, "Cerrar caja", "Caja"),
        new(CajaMovimientos, "Registrar ingresos de efectivo", "Caja"),
        new(CajaRetiro, "Registrar retiros de efectivo", "Caja"),
        new(VentasCrear, "Registrar ventas", "Ventas"),
        new(VentasVer, "Ver ventas", "Ventas"),
        new(VentasAnular, "Anular ventas", "Ventas"),
        new(VentasDescuento, "Aplicar descuentos en la venta", "Ventas"),
        new(DevolucionesCrear, "Registrar devoluciones", "Ventas"),
        new(DevolucionesAprobar, "Aprobar devoluciones y reembolsos", "Ventas"),
        new(PagosResolver, "Resolver a mano pagos sin respuesta de la pasarela", "Pagos"),
        new(FiscalGestionar, "Gestionar facturación electrónica", "Facturación"),
    ];
}

public sealed record PermissionDefinition(string Code, string Name, string Module);
