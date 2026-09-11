using CaCo.Application.Configuration;
using CaCo.Application.Errors;
using CaCo.Application.Import;
using CaCo.Application.Repositories;
using CaCo.Application.Search;
using CaCo.Application.Services;
using CaCo.Application.Storage;
using CaCo.Core;
using CaCo.Infrastructure.Configuration;
using CaCo.Infrastructure.Errors;
using CaCo.Infrastructure.Import;
using CaCo.Infrastructure.Logging;
using CaCo.Infrastructure.Persistence.Sqlite;
using CaCo.Infrastructure.Persistence;
using CaCo.Infrastructure.Search;
using CaCo.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CaCo.Infrastructure.DependencyInjection;

/// <summary>
/// Composición de dependencias de CA-CO (Composition Root compartido).
/// La app WinUI llama a <see cref="AddCaco"/> una sola vez al arrancar;
/// los tests la usan para pruebas de integración.
/// </summary>
public static class CacoServiceCollectionExtensions
{
    /// <summary>Registra todos los servicios de CA-CO.</summary>
    /// <param name="services">Colección de servicios.</param>
    /// <param name="libraryRoot">Raíz de la biblioteca (resuelta con <see cref="LibraryRootResolver"/>).</param>
    /// <param name="configure">Personalización opcional (tests).</param>
    public static IServiceCollection AddCaco(
        this IServiceCollection services,
        string libraryRoot,
        Action<CacoServiceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);

        var options = new CacoServiceOptions();
        configure?.Invoke(options);

        // Núcleo transversal.
        services.AddSingleton<IClock>(SystemClock.Instance);

        // Configuración (instancia inmutable compartida).
        services.AddSingleton(options.Settings);

        // Almacenamiento y persistencia (Fase 1: SQLite; JSON queda para migración y tests).
        services.AddSingleton<ILibraryPaths>(_ => new LibraryPaths(libraryRoot));
        services.AddSingleton<ILibraryInitializer, LibraryInitializer>();
        services.AddSingleton<IFileStorage, PhysicalFileStorage>();
        services.AddSingleton<LegacyVfsMigrator>();
        services.AddSingleton(sp => new SqliteDatabase(
            Path.Combine(sp.GetRequiredService<ILibraryPaths>().Database, "caco.db")));
        services.AddSingleton<IDocumentRepository, SqliteDocumentRepository>();
        services.AddSingleton<INotebookRepository, SqliteNotebookRepository>();
        services.AddSingleton<INoteRepository, SqliteNoteRepository>();
        services.AddSingleton<ITagRepository, SqliteTagRepository>();
        services.AddSingleton<JsonToSqliteMigrator>();
        services.AddSingleton<IThumbnailService, NullThumbnailService>();
        services.AddSingleton<IAppConfiguration>(sp =>
            new FileAppConfiguration(options.SettingsFile));

        // Importación y búsqueda.
        services.AddSingleton<IFileTypeValidator, FileTypeValidator>();
        services.AddSingleton<IDocumentImporter, LocalDocumentImporter>();
        services.AddSingleton<ISearchService, BasicSearchService>();

        // Casos de uso.
        services.AddSingleton<IDocumentService, DocumentService>();
        services.AddSingleton<INotebookService, NotebookService>();
        services.AddSingleton<INoteService, NoteService>();
        services.AddSingleton<ILibraryService, LibraryService>();

        // Errores.
        services.AddSingleton<IErrorHandler, AppErrorHandler>();

        return services;
    }

    /// <summary>Registra logging de CA-CO (consola en Debug + archivo).</summary>
    public static IServiceCollection AddCacoLogging(this IServiceCollection services, string? logsFolder = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddCacoFileLogging(logsFolder);
        });
        return services;
    }
}

/// <summary>Opciones de composición para tests y arranque.</summary>
public sealed class CacoServiceOptions
{
    /// <summary>Ajustes compartidos (los cargados desde disco al arrancar).</summary>
    public CacoSettings Settings { get; set; } = new();

    /// <summary>Ruta del archivo de ajustes (tests usan una temporal).</summary>
    public string? SettingsFile { get; set; }
}
