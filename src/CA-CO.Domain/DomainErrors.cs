using CaCo.Core;

namespace CaCo.Domain;

/// <summary>
/// Errores de dominio con códigos estables. La UI los traduce a mensajes
/// entendibles; nunca se muestra <see cref="Error.Message"/> tal cual al usuario.
/// </summary>
public static class DomainErrors
{
    /// <summary>Errores de documentos.</summary>
    public static class Document
    {
        /// <summary>El documento no existe.</summary>
        public static Error NotFound(Guid id) =>
            Error.NotFound("Document.NotFound", $"No existe el documento '{id}'.");

        /// <summary>Nombre inválido.</summary>
        public static Error InvalidName(string reason) =>
            Error.Validation("Document.InvalidName", $"Nombre de documento inválido: {reason}.");

        /// <summary>Tamaño o hash inválido.</summary>
        public static Error InvalidSize(string reason) =>
            Error.Validation("Document.InvalidSize", $"Tamaño de documento inválido: {reason}.");

        /// <summary>Tipo de archivo no soportado.</summary>
        public static Error UnsupportedType(string extension) =>
            Error.Validation("Document.UnsupportedType", $"Tipo de archivo no soportado: '{extension}'.");

        /// <summary>Ya está en el estado solicitado.</summary>
        public static Error AlreadyInState(string state) =>
            Error.Conflict("Document.AlreadyInState", $"El documento ya está en estado '{state}'.");
    }

    /// <summary>Errores de cuadernos.</summary>
    public static class Notebook
    {
        /// <summary>El cuaderno no existe.</summary>
        public static Error NotFound(Guid id) =>
            Error.NotFound("Notebook.NotFound", $"No existe el cuaderno '{id}'.");

        /// <summary>Nombre inválido.</summary>
        public static Error InvalidName(string reason) =>
            Error.Validation("Notebook.InvalidName", $"Nombre de cuaderno inválido: {reason}.");

        /// <summary>Un cuaderno no puede ser su propio padre.</summary>
        public static Error SelfParent() =>
            Error.Validation("Notebook.SelfParent", "Un cuaderno no puede contenerse a sí mismo.");

        /// <summary>Mover aquí crearía un ciclo en la jerarquía.</summary>
        public static Error Cycle() =>
            Error.Validation("Notebook.Cycle", "La operación crearía un ciclo en la jerarquía de cuadernos.");

        /// <summary>El cuaderno padre no existe.</summary>
        public static Error ParentNotFound(Guid id) =>
            Error.NotFound("Notebook.ParentNotFound", $"No existe el cuaderno padre '{id}'.");
    }

    /// <summary>Errores de notas.</summary>
    public static class Note
    {
        /// <summary>La nota no existe.</summary>
        public static Error NotFound(Guid id) =>
            Error.NotFound("Note.NotFound", $"No existe la nota '{id}'.");
    }

    /// <summary>Errores de etiquetas.</summary>
    public static class Tag
    {
        /// <summary>Nombre de etiqueta inválido.</summary>
        public static Error InvalidName() =>
            Error.Validation("Tag.InvalidName", "El nombre de la etiqueta no es válido.");

        /// <summary>La etiqueta no existe.</summary>
        public static Error NotFound(Guid id) =>
            Error.NotFound("Tag.NotFound", $"No existe la etiqueta '{id}'.");
    }

    /// <summary>
    /// Sanea texto visible (nombres, títulos, etiquetas): elimina controles y
    /// formatos bidi (U+202A-202E, U+2066-2069, ZWSP, etc.) que permiten spoof.
    /// </summary>
    internal static class TextSanitizer
    {
        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sb = new System.Text.StringBuilder(value.Length);
            foreach (var ch in value.Normalize(System.Text.NormalizationForm.FormKC))
            {
                if (ch is '\t' or '\n' or '\r')
                {
                    sb.Append(ch);
                    continue;
                }

                var cat = char.GetUnicodeCategory(ch);
                if (cat is System.Globalization.UnicodeCategory.Control
                    or System.Globalization.UnicodeCategory.Format
                    or System.Globalization.UnicodeCategory.Surrogate
                    or System.Globalization.UnicodeCategory.PrivateUse)
                {
                    continue;
                }

                if (ch is '\u200B' or '\u200C' or '\u200D' or '\uFEFF'
                    or >= '\u202A' and <= '\u202E'
                    or >= '\u2066' and <= '\u2069'
                    or >= '\u200E' and <= '\u200F')
                {
                    continue;
                }

                sb.Append(ch);
            }

            return sb.ToString().Trim();
        }
    }
}
