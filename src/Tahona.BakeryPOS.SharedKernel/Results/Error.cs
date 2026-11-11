namespace Tahona.BakeryPOS.SharedKernel.Results;

/// <summary>
/// Business error with a stable code (API contract, docs/api-contracts.md).
/// Each new code is added here and in ResultExtensions (its HTTP status).
/// </summary>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    // Authentication and permissions
    public static readonly Error Unauthenticated = new("UNAUTHENTICATED", "Credenciales inválidas o sesión expirada.");
    public static readonly Error PermissionDenied = new("PERMISSION_DENIED", "No tiene permisos para realizar esta acción.");
    public static readonly Error CompanyRequired = new("COMPANY_REQUIRED", "La operación requiere una sesión dentro de una empresa.");
    public static readonly Error PasswordChangeRequired = new("PASSWORD_CHANGE_REQUIRED", "Debes cambiar la contraseña de un solo uso antes de continuar.");
    public static readonly Error IncorrectPassword = new("INCORRECT_PASSWORD", "La contraseña actual no es correcta.");
    public static readonly Error DeviceNotPaired = new("DEVICE_NOT_PAIRED", "Este equipo no está vinculado a una caja activa. Vincúlalo de nuevo desde el backoffice.");
    public static readonly Error InvalidPairingCode = new("INVALID_PAIRING_CODE", "El código de vinculación no existe, ya se usó o venció. Genera uno nuevo en el backoffice.");
    public static readonly Error NoCurrentAssignment = new("PERMISSION_DENIED", "No tienes una asignación vigente en esta sucursal.");

    public static Error UserLocked(DateTime until) =>
        new("USER_LOCKED", $"Demasiados intentos fallidos. Intenta de nuevo después de las {until:HH:mm} UTC.");

    // Resources and concurrency
    public static readonly Error ResourceNotFound = new("NOT_FOUND", "El recurso solicitado no fue encontrado.");
    public static readonly Error VersionConflict = new("VERSION_CONFLICT", "El registro fue modificado por otro usuario. Recárgalo e intenta de nuevo.");
    public static readonly Error VersionRequired = new("VERSION_REQUIRED", "Envía el encabezado If-Match con la versión (ETag) que leíste.");
    public static readonly Error DuplicateCode = new("DUPLICATE_CODE", "Ya existe un registro con ese código en la empresa.");
    public static readonly Error DuplicateNit = new("DUPLICATE_NIT", "Ya existe una empresa con ese NIT en la plataforma.");
    public static readonly Error EmailInUse = new("EMAIL_IN_USE", "Ese usuario ya pertenece a la empresa.");
    public static readonly Error DuplicateAssignment = new("DUPLICATE_ASSIGNMENT", "El usuario ya tiene ese rol vigente en esa sucursal.");
    public static readonly Error LastOwner = new("LAST_OWNER", "La empresa debe conservar al menos un propietario activo.");
    public static readonly Error SharedUser = new("SHARED_USER", "El usuario pertenece a otras empresas: solo él puede cambiar su nombre.");
    public static readonly Error SelfDeactivation = new("SELF_DEACTIVATION", "No puedes desactivar tu propio usuario.");

    // Inventory
    public static readonly Error InsufficientStock = new("INSUFFICIENT_STOCK", "No hay existencias suficientes para el movimiento.");
    public static readonly Error InitialStockAlreadyRecorded = new("INITIAL_STOCK_ALREADY_RECORDED", "Ese producto ya tiene existencia inicial en esa ubicación; usa un ajuste.");

    // Cash
    public static readonly Error CashSessionAlreadyOpen = new("CASH_SESSION_ALREADY_OPEN", "Esta caja ya tiene una sesión abierta.");
    public static readonly Error CashSessionClosed = new("CASH_SESSION_CLOSED", "La sesión de caja ya está cerrada.");
    public static readonly Error WithdrawalExceedsBalance = new("WITHDRAWAL_EXCEEDS_BALANCE", "El retiro supera el efectivo esperado en la caja.");

    public static Error PaymentPending(int count) =>
        new("PAYMENT_PENDING", $"Hay {count} pago(s) pendiente(s) en esta sesión: resuélvelos antes de cerrar la caja (D-12).");

    // Payments
    public static readonly Error GatewayNotConfigured = new("GATEWAY_NOT_CONFIGURED", "La empresa no tiene la pasarela de pagos configurada y activa.");
    public static readonly Error TerminalNotConfigured = new("TERMINAL_NOT_CONFIGURED", "Esta caja no tiene un datáfono Bold asignado.");
    public static readonly Error PaymentInProgress = new("PAYMENT_IN_PROGRESS", "Este pago ya tiene un cobro en curso en el datáfono.");
    /// <summary>D-11: a void only proceeds while the sale's shift is still open and with no returns.</summary>
    public static readonly Error VoidNotAllowed = new("VOID_NOT_ALLOWED", "La venta ya no se puede anular: registra una devolución.");
    public static readonly Error ProviderUnavailable = new("PROVIDER_UNAVAILABLE", "La pasarela no respondió. El cobro quedó pendiente: revisa el datáfono antes de reintentar.");

    // Electronic invoicing
    public static readonly Error InvoicingNotConfigured = new("INVOICING_NOT_CONFIGURED", "La facturación electrónica de la empresa no está configurada y activa.");

    // Idempotency
    public static readonly Error IdempotencyKeyRequired = new("IDEMPOTENCY_KEY_REQUIRED", "Esta operación exige el encabezado Idempotency-Key.");
    public static readonly Error IdempotencyConflict = new("IDEMPOTENCY_CONFLICT", "La clave de idempotencia ya se usó con un contenido distinto.");
    public static readonly Error IdempotencyInProgress = new("IDEMPOTENCY_IN_PROGRESS", "Una petición con la misma clave de idempotencia aún se está procesando.");

    // Validation and limits
    public static readonly Error InvalidData = new("VALIDATION", "Los datos proporcionados no son válidos.");
    public static readonly Error RateLimitExceeded = new("RATE_LIMIT_EXCEEDED", "Se ha excedido el límite permitido.");

    public static Error NotFound(string entity, Guid id) =>
        new("NOT_FOUND", $"{entity} con ID '{id}' no fue encontrado.");

    public static Error Validation(string message) =>
        new("VALIDATION", message);
}
