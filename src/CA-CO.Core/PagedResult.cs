namespace CaCo.Core;

/// <summary>
/// Página de resultados para consultas potencialmente grandes.
/// La biblioteca puede crecer a decenas de miles de documentos: ningún caso de uso
/// debe cargar la biblioteca completa en memoria. Fase 0 ya pagina en repositorios.
/// </summary>
/// <typeparam name="T">Tipo de elemento.</typeparam>
public sealed class PagedResult<T>
{
    /// <summary>Elementos de la página solicitada.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Número total de elementos (sin paginar).</summary>
    public required int TotalCount { get; init; }

    /// <summary>Número de página (base 1).</summary>
    public required int Page { get; init; }

    /// <summary>Tamaño de página.</summary>
    public required int PageSize { get; init; }

    /// <summary>Indica si hay más páginas.</summary>
    public bool HasNextPage => (long)Page * PageSize < TotalCount;

    /// <summary>Crea una página vacía.</summary>
    public static PagedResult<T> Empty(int page = 1, int pageSize = 50) =>
        new() { Items = [], Page = page, PageSize = pageSize, TotalCount = 0 };
}
