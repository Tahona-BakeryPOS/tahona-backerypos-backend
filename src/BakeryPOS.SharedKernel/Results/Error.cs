namespace BakeryPOS.SharedKernel.Results;

public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    // Auth errors
    public static readonly Error NoAutenticado = new("NO_AUTENTICADO", "Credenciales inválidas o sesión expirada.");
    public static readonly Error SinPermiso = new("SIN_PERMISO", "No tiene permisos para realizar esta acción.");

    // Resource errors
    public static readonly Error NoEncontrado = new("NO_ENCONTRADO", "El recurso solicitado no fue encontrado.");
    public static readonly Error ConflictoVersion = new("CONFLICTO_VERSION", "El registro fue modificado por otro usuario. Reintente la operación.");

    // Sale errors
    public static readonly Error VentaDuplicadaDistinta = new("VENTA_DUPLICADA_DISTINTA", "Ya existe una venta con el mismo ID pero con datos diferentes.");
    public static readonly Error PagosNoCubrenTotal = new("PAGOS_NO_CUBREN_TOTAL", "La suma de los pagos no cubre el total de la venta.");

    // Cash shift errors
    public static readonly Error TurnoYaAbierto = new("TURNO_YA_ABIERTO", "Ya existe un turno abierto para esta caja.");
    public static readonly Error TurnoCerrado = new("TURNO_CERRADO", "El turno de caja ya fue cerrado.");

    // Validation errors
    public static readonly Error Validacion = new("VALIDACION", "Los datos proporcionados no son válidos.");
    public static readonly Error LimiteExcedido = new("LIMITE_EXCEDIDO", "Se ha excedido el límite permitido.");

    // Payment errors (CU-04)
    public static readonly Error PagoExcedeTotal = new("PAGO_EXCEDE_TOTAL", "Un pago distinto a efectivo no puede exceder el monto pendiente.");
    public static readonly Error ReferenciaRequerida = new("REFERENCIA_REQUERIDA", "El medio de pago seleccionado requiere número de referencia.");

    // Cash movement errors (CU-06)
    public static readonly Error RetiroExcedeSaldo = new("RETIRO_EXCEDE_SALDO", "El retiro excede el efectivo esperado en el turno.");

    // Catalog errors (CU-09)
    public static readonly Error CodigoDuplicado = new("CODIGO_DUPLICADO", "Ya existe un registro con ese código dentro de la organización.");

    // Sync errors (CU-11)
    public static readonly Error VentaRechazadaSync = new("VENTA_RECHAZADA_SYNC", "La venta fue rechazada durante la sincronización.");

    public static Error NotFound(string entity, Guid id) =>
        new("NO_ENCONTRADO", $"{entity} con ID '{id}' no fue encontrado.");

    public static Error Validation(string message) =>
        new("VALIDACION", message);
}
