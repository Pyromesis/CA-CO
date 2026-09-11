using System.Collections.Concurrent;
using System.Text;
using CaCo.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace CaCo.Infrastructure.Logging;

/// <summary>
/// Proveedor de logging a archivo para una app de escritorio.
/// Escribe en <c>%LOCALAPPDATA%\CA-CO\logs\caco-AAAAMMDD.log</c> con rotación
/// por tamaño (5 MB). Nunca registra contenido de documentos: solo eventos,
/// errores y métricas. Sin telemetría ni red (offline-first).
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _folder;
    private readonly LogLevel _minLevel;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>Crea el proveedor.</summary>
    /// <param name="folder">Carpeta de logs (por defecto, la de la aplicación).</param>
    /// <param name="minLevel">Nivel mínimo a persistir.</param>
    public FileLoggerProvider(string? folder = null, LogLevel minLevel = LogLevel.Information)
    {
        _folder = folder ?? AppPaths.LogsFolder;
        _minLevel = minLevel;
        try
        {
            Directory.CreateDirectory(_folder);
        }
        catch
        {
            // El logging nunca debe tumbar la app: si no se puede crear la carpeta,
            // los Write posteriores fallarán de forma silenciosa.
        }
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
        _loggers.Clear();
    }

    internal bool IsEnabled(LogLevel level) => level >= _minLevel && !_disposed;

    internal void Write(string category, LogLevel level, string message, Exception? exception)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        var file = Path.Combine(_folder, $"caco-{DateTime.Now:yyyyMMdd}.log");
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture))
            .Append(" [").Append(level.ToString().ToUpperInvariant().PadRight(7)).Append("] ")
            .Append(category).Append(": ").Append(message);

        if (exception is not null)
        {
            line.Append(" => ").Append(exception.GetType().Name).Append(": ").Append(exception.Message)
                .AppendLine().Append(exception.StackTrace);
        }

        line.AppendLine();

        try
        {
            lock (_gate)
            {
                RotateIfNeeded(file);
                File.AppendAllText(file, line.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // El logging es best-effort: un disco lleno o sin permiso no debe lanzar.
        }
    }

    private static void RotateIfNeeded(string file)
    {
        const long maxBytes = 5 * 1024 * 1024;
        try
        {
            if (File.Exists(file) && new FileInfo(file).Length > maxBytes)
            {
                var backup = file + ".1";
                File.Move(file, backup, overwrite: true);
            }
        }
        catch
        {
            // Si la rotación falla, se sigue escribiendo en el archivo actual.
        }
    }

    private sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}

/// <summary>Extensiones para registrar el logging de CA-CO.</summary>
public static class FileLoggerExtensions
{
    /// <summary>Añade el proveedor de archivo al builder de logging.</summary>
    public static ILoggingBuilder AddCacoFileLogging(
        this ILoggingBuilder builder,
        string? folder = null,
        LogLevel minLevel = LogLevel.Information)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddProvider(new FileLoggerProvider(folder, minLevel));
        return builder;
    }
}
