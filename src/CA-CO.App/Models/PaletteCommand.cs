namespace CaCo.App.Models;

/// <summary>Comando de la paleta (Ctrl+K): título, pista y acción a ejecutar.</summary>
/// <param name="Title">Título visible (lo que se filtra).</param>
/// <param name="Subtitle">Descripción secundaria.</param>
/// <param name="IconGlyph">Glifo de Segoe Fluent Icons.</param>
/// <param name="Shortcut">Atajo mostrado a la derecha (puede estar vacío).</param>
/// <param name="Action">Acción que se ejecuta al elegirlo.</param>
public sealed record PaletteCommand(string Title, string Subtitle, string IconGlyph, string Shortcut, Action? Action);
