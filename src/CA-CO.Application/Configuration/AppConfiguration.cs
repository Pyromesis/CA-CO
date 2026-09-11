namespace CaCo.Application.Configuration;

/// <summary>
/// Ajustes de CA-CO. Las secciones de fases futuras existen desde Fase 0 para que la
/// página de Configuración pueda mostrarlas como "próximamente", pero están
/// deshabilitadas y marcadas como no disponibles.
/// </summary>
public sealed class CacoSettings
{
    /// <summary>Ruta personalizada de la biblioteca. Vacía = ubicación por defecto.</summary>
    public string LibraryPath { get; set; } = string.Empty;

    /// <summary>
    /// Token de acceso a la carpeta elegida (FutureAccessList de Windows).
    /// Permite usar una carpeta elegida por el usuario en app empaquetada sin
    /// capacidades restringidas. Vacío = sin carpeta brokered.
    /// </summary>
    public string LibraryToken { get; set; } = string.Empty;

    /// <summary>Ajustes generales.</summary>
    public GeneralSettings General { get; set; } = new();

    /// <summary>Ajustes de almacenamiento.</summary>
    public StorageSettings Storage { get; set; } = new();

    /// <summary>Ajustes de privacidad.</summary>
    public PrivacySettings Privacy { get; set; } = new();

    /// <summary>Ajustes de seguridad (Fase 8).</summary>
    public SecuritySettings Security { get; set; } = new();

    /// <summary>Ajustes de OCR (Fase 5).</summary>
    public OcrSettings Ocr { get; set; } = new();

    /// <summary>Ajustes de voz (Fase 7).</summary>
    public VoiceSettings Voice { get; set; } = new();

    /// <summary>Ajustes de nube (Fase 10).</summary>
    public CloudSettings Cloud { get; set; } = new();
}

/// <summary>Ajustes generales.</summary>
public sealed class GeneralSettings
{
    /// <summary>Idioma de la interfaz (p. ej. "es-ES").</summary>
    public string Language { get; set; } = "es-ES";

    /// <summary>Tema: System, Light, Dark.</summary>
    public string Theme { get; set; } = "Light";
}

/// <summary>Ajustes de almacenamiento local.</summary>
public sealed class StorageSettings
{
    /// <summary>Conservar copia exacta del original al importar.</summary>
    public bool KeepOriginalCopy { get; set; } = true;

    /// <summary>Días antes de purgar <c>Temp</c> automáticamente.</summary>
    public int TempRetentionDays { get; set; } = 7;
}

/// <summary>Ajustes de privacidad (offline-first: sin telemetría).</summary>
public sealed class PrivacySettings
{
    /// <summary>CA-CO no envía telemetría. Reservado para auditoría futura.</summary>
    public bool TelemetryEnabled { get; set; }
}

/// <summary>Ajustes de seguridad. Reservado para Fase 8 (PIN, contraseña, Windows Hello, cifrado).</summary>
public sealed class SecuritySettings
{
    /// <summary>Disponible a partir de Fase 8.</summary>
    public bool Available { get; } = false;

    /// <summary>Bloqueo de la aplicación.</summary>
    public bool LockEnabled { get; set; }

    /// <summary>Cifrado de la biblioteca.</summary>
    public bool EncryptionEnabled { get; set; }
}

/// <summary>Ajustes de OCR local. Reservado para Fase 5.</summary>
public sealed class OcrSettings
{
    /// <summary>Disponible a partir de Fase 5.</summary>
    public bool Available { get; } = false;

    /// <summary>OCR automático al importar.</summary>
    public bool AutoOcrOnImport { get; set; }
}

/// <summary>Ajustes de dictado local. Reservado para Fase 7.</summary>
public sealed class VoiceSettings
{
    /// <summary>Disponible a partir de Fase 7.</summary>
    public bool Available { get; } = false;
}

/// <summary>Ajustes de proveedores nube. Reservado para Fase 10.</summary>
public sealed class CloudSettings
{
    /// <summary>Disponible a partir de Fase 10.</summary>
    public bool Available { get; } = false;

    /// <summary>Proveedor: None, OneDrive, GoogleDrive, Dropbox.</summary>
    public string Provider { get; set; } = "None";
}

/// <summary>
/// Carga y guarda los ajustes. Implementación en Infrastructure (JSON en LocalAppData).
/// </summary>
public interface IAppConfiguration
{
    /// <summary>Carga los ajustes (devuelve valores por defecto si no existen).</summary>
    Task<CacoSettings> LoadAsync(CancellationToken ct);

    /// <summary>Guarda los ajustes.</summary>
    Task SaveAsync(CacoSettings settings, CancellationToken ct);
}
