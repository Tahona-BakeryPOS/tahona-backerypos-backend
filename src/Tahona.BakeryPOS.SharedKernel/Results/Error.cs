namespace Tahona.BakeryPOS.SharedKernel.Results;

/// <summary>
/// Business error with a stable code (API contract, docs/api-contracts.md).
/// Each new code is added here and in ResultExtensions (its HTTP status).
/// </summary>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    // Authentication and permissions
    public static readonly Error NoAutenticado = new("NO_AUTENTICADO", "Credenciales inválidas o sesión expirada.");
    public static readonly Error SinPermiso = new("SIN_PERMISO", "No tiene permisos para realizar esta acción.");
    public static readonly Error EmpresaRequerida = new("EMPRESA_REQUERIDA", "La operación requiere una sesión dentro de una empresa.");
    public static readonly Error CambioContrasenaRequerido = new("CAMBIO_CONTRASENA_REQUERIDO", "Debes cambiar la contraseña de un solo uso antes de continuar.");
    public static readonly Error ContrasenaIncorrecta = new("CONTRASENA_INCORRECTA", "La contraseña actual no es correcta.");
    public static readonly Error DispositivoNoVinculado = new("DISPOSITIVO_NO_VINCULADO", "Este equipo no está vinculado a una caja activa. Vincúlalo de nuevo desde el backoffice.");
    public static readonly Error CodigoVinculacionInvalido = new("CODIGO_VINCULACION_INVALIDO", "El código de vinculación no existe, ya se usó o venció. Genera uno nuevo en el backoffice.");
    public static readonly Error SinAsignacionVigente = new("SIN_PERMISO", "No tienes una asignación vigente en esta sucursal.");

    public static Error UsuarioBloqueado(DateTime until) =>
        new("USUARIO_BLOQUEADO", $"Demasiados intentos fallidos. Intenta de nuevo después de las {until:HH:mm} UTC.");

    // Recursos y concurrencia
    public static readonly Error NoEncontrado = new("NO_ENCONTRADO", "El recurso solicitado no fue encontrado.");
    public static readonly Error ConflictoVersion = new("CONFLICTO_VERSION", "El registro fue modificado por otro usuario. Recárgalo e intenta de nuevo.");
    public static readonly Error VersionRequerida = new("VERSION_REQUERIDA", "Envía el encabezado If-Match con la versión (ETag) que leíste.");
    public static readonly Error CodigoDuplicado = new("CODIGO_DUPLICADO", "Ya existe un registro con ese código en la empresa.");
    public static readonly Error NitDuplicado = new("NIT_DUPLICADO", "Ya existe una empresa con ese NIT en la plataforma.");
    public static readonly Error CorreoEnUso = new("CORREO_EN_USO", "Ese usuario ya pertenece a la empresa.");
    public static readonly Error AsignacionDuplicada = new("ASIGNACION_DUPLICADA", "El usuario ya tiene ese rol vigente en esa sucursal.");
    public static readonly Error UltimoPropietario = new("ULTIMO_PROPIETARIO", "La empresa debe conservar al menos un propietario activo.");
    public static readonly Error UsuarioCompartido = new("USUARIO_COMPARTIDO", "El usuario pertenece a otras empresas: solo él puede cambiar su nombre.");
    public static readonly Error AutoDesactivacion = new("AUTO_DESACTIVACION", "No puedes desactivar tu propio usuario.");

    // Inventario
    public static readonly Error ExistenciasInsuficientes = new("EXISTENCIAS_INSUFICIENTES", "No hay existencias suficientes para el movimiento.");
    public static readonly Error InventarioInicialRegistrado = new("INVENTARIO_INICIAL_REGISTRADO", "Ese producto ya tiene existencia inicial en esa ubicación; usa un ajuste.");

    // Caja
    public static readonly Error CajaYaAbierta = new("CAJA_YA_ABIERTA", "Esta caja ya tiene una sesión abierta.");
    public static readonly Error CajaCerrada = new("CAJA_CERRADA", "La sesión de caja ya está cerrada.");
    public static readonly Error RetiroExcedeSaldo = new("RETIRO_EXCEDE_SALDO", "El retiro supera el efectivo esperado en la caja.");

    public static Error PagoPendiente(int count) =>
        new("PAGO_PENDIENTE", $"Hay {count} pago(s) pendiente(s) en esta sesión: resuélvelos antes de cerrar la caja (D-12).");

    // Pagos
    public static readonly Error PasarelaNoConfigurada = new("PASARELA_NO_CONFIGURADA", "La empresa no tiene la pasarela de pagos configurada y activa.");
    public static readonly Error DatafonoNoConfigurado = new("DATAFONO_NO_CONFIGURADO", "Esta caja no tiene un datáfono Bold asignado.");
    public static readonly Error PagoEnCurso = new("PAGO_EN_CURSO", "Este pago ya tiene un cobro en curso en el datáfono.");
    /// <summary>D-11: a void only proceeds while the sale's shift is still open and with no returns.</summary>
    public static readonly Error AnulacionNoPermitida = new("ANULACION_NO_PERMITIDA", "La venta ya no se puede anular: registra una devolución.");
    public static readonly Error ProveedorNoDisponible = new("PROVEEDOR_NO_DISPONIBLE", "La pasarela no respondió. El cobro quedó pendiente: revisa el datáfono antes de reintentar.");

    // Electronic invoicing
    public static readonly Error FacturacionNoConfigurada = new("FACTURACION_NO_CONFIGURADA", "La facturación electrónica de la empresa no está configurada y activa.");

    // Idempotencia
    public static readonly Error IdempotenciaRequerida = new("IDEMPOTENCIA_REQUERIDA", "Esta operación exige el encabezado Idempotency-Key.");
    public static readonly Error IdempotenciaConflicto = new("IDEMPOTENCIA_CONFLICTO", "La clave de idempotencia ya se usó con un contenido distinto.");
    public static readonly Error IdempotenciaEnCurso = new("IDEMPOTENCIA_EN_CURSO", "Una petición con la misma clave de idempotencia aún se está procesando.");

    // Validation and limits
    public static readonly Error Validacion = new("VALIDACION", "Los datos proporcionados no son válidos.");
    public static readonly Error LimiteExcedido = new("LIMITE_EXCEDIDO", "Se ha excedido el límite permitido.");

    public static Error NotFound(string entity, Guid id) =>
        new("NO_ENCONTRADO", $"{entity} con ID '{id}' no fue encontrado.");

    public static Error Validation(string message) =>
        new("VALIDACION", message);
}
