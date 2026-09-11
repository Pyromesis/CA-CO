namespace CaCo.Core;

/// <summary>
/// Representa un error de dominio o aplicación sin usar excepciones para el flujo esperado.
/// </summary>
/// <param name="Code">Código estable y programático del error (p. ej. "Document.NotFound").</param>
/// <param name="Message">Mensaje técnico (no mostrar tal cual al usuario; ver <c>IErrorHandler</c>).</param>
public sealed record Error(string Code, string Message)
{
    /// <summary>Error genérico cuando no hay uno más específico.</summary>
    public static readonly Error None = new("None", "Sin error.");

    /// <summary>Crea un error de validación.</summary>
    public static Error Validation(string code, string message) => new(code, message);

    /// <summary>Crea un error de "no encontrado".</summary>
    public static Error NotFound(string code, string message) => new(code, message);

    /// <summary>Crea un error de conflicto (duplicados, estados inválidos...).</summary>
    public static Error Conflict(string code, string message) => new(code, message);

    /// <summary>Crea un error de infraestructura (disco, permisos...).</summary>
    public static Error Storage(string code, string message) => new(code, message);

    /// <inheritdoc/>
    public override string ToString() => $"{Code}: {Message}";
}
