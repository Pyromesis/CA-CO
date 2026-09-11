using CaCo.Core;

namespace CaCo.Application.Future.Ocr;

/// <summary>
/// OCR local (Reservado para Fase 5). Extrae texto de PDFs e imágenes sin salir del equipo.
/// </summary>
public interface IOcrService
{
    /// <summary>Indica si hay un motor OCR disponible.</summary>
    bool IsAvailable { get; }

    /// <summary>Extrae el texto de un documento.</summary>
    Task<Result<OcrResult>> RecognizeAsync(Guid documentId, CancellationToken ct);
}

/// <summary>Motor OCR concreto (p. ej. Tesseract local, Windows OCR). Fase 5.</summary>
public interface IOcrEngine
{
    /// <summary>Nombre del motor.</summary>
    string Name { get; }

    /// <summary>Reconoce texto de un archivo.</summary>
    Task<Result<OcrResult>> RecognizeFileAsync(string filePath, string language, CancellationToken ct);
}

/// <summary>Resultado de un reconocimiento OCR.</summary>
/// <param name="Text">Texto extraído.</param>
/// <param name="Confidence">Confianza media 0-1.</param>
/// <param name="EngineName">Motor utilizado.</param>
public sealed record OcrResult(string Text, double Confidence, string EngineName);
