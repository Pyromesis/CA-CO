using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CaCo.App.Services;

/// <summary>Diálogos modales de la UI (confirmaciones, entradas de texto, avisos).</summary>
public interface IDialogService
{
    /// <summary>
    /// Fija la ventana propietaria (lo llama <c>MainWindow</c> al arrancar).
    /// Se guarda la ventana (no el <c>XamlRoot</c>): el XamlRoot aún no existe
    /// durante la construcción y solo es válido con la ventana activada.
    /// </summary>
    void Initialize(Window window);

    /// <summary>Pide confirmación sí/no.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "Aceptar");

    /// <summary>Pide un texto (crear/renombrar). <c>null</c> si se cancela.</summary>
    Task<string?> PromptTextAsync(string title, string placeholder, string initialText = "");

    /// <summary>Pide un texto largo (editar comentario). <c>null</c> si se cancela.</summary>
    Task<string?> PromptMultilineTextAsync(string title, string placeholder, string initialText = "");

    /// <summary>Pide elegir una opción de una lista. <c>null</c> si se cancela.</summary>
    Task<T?> PromptChoiceAsync<T>(string title, IList<T> options, Func<T, string> display) where T : class;

    /// <summary>Muestra un aviso.</summary>
    Task ShowMessageAsync(string title, string message);

    /// <summary>Muestra el texto extraído por OCR con acciones.</summary>
    Task<OcrAction> ShowOcrResultAsync(string text);
}

/// <summary>Acción elegida sobre el texto OCR.</summary>
public enum OcrAction
{
    /// <summary>Cancelar/cerrar.</summary>
    Cancel = 0,

    /// <summary>Copiar al portapapeles.</summary>
    Copy = 1,

    /// <summary>Guardar como comentario.</summary>
    SaveComment = 2,

    /// <summary>Guardar como documento TXT.</summary>
    SaveTxt = 3,
}

/// <summary>Implementación con <c>ContentDialog</c> nativo.</summary>
public sealed class DialogService : IDialogService
{
    private Window? _window;

    /// <inheritdoc/>
    public void Initialize(Window window) => _window = window;

    /// <inheritdoc/>
    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "Aceptar")
    {
        EnsureInitialized();
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root(),
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <inheritdoc/>
    public async Task<string?> PromptTextAsync(string title, string placeholder, string initialText = "")
    {
        EnsureInitialized();
        var textBox = new TextBox
        {
            PlaceholderText = placeholder,
            Text = initialText,
            MinWidth = 280,
        };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = textBox,
            PrimaryButtonText = "Aceptar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root(),
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var value = textBox.Text?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <inheritdoc/>
    public async Task<string?> PromptMultilineTextAsync(string title, string placeholder, string initialText = "")
    {
        EnsureInitialized();
        var textBox = new TextBox
        {
            PlaceholderText = placeholder,
            Text = initialText,
            MinWidth = 320,
            MinHeight = 120,
            AcceptsReturn = true,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
        };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = textBox,
            PrimaryButtonText = "Aceptar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root(),
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var value = textBox.Text?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <inheritdoc/>
    public async Task<T?> PromptChoiceAsync<T>(string title, IList<T> options, Func<T, string> display)
        where T : class
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(display);

        var comboBox = new ComboBox
        {
            Header = title,
            MinWidth = 280,
            ItemsSource = options.Select(display).ToList(),
            SelectedIndex = options.Count > 0 ? 0 : -1,
        };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = comboBox,
            PrimaryButtonText = "Aceptar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root(),
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var index = comboBox.SelectedIndex;
        return index >= 0 && index < options.Count ? options[index] : null;
    }

    /// <inheritdoc/>
    public async Task<OcrAction> ShowOcrResultAsync(string text)
    {
        EnsureInitialized();
        var choice = OcrAction.Cancel;

        var preview = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            MaxHeight = 300,
            MinWidth = 320,
        };
        var copyButton = new Button { Content = "Copiar" };
        var commentButton = new Button { Content = "Guardar como comentario" };
        var txtButton = new Button { Content = "Guardar como TXT" };
        var buttons = new StackPanel
        {
            Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
            Spacing = 8,
        };
        buttons.Children.Add(copyButton);
        buttons.Children.Add(commentButton);
        buttons.Children.Add(txtButton);

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(preview);
        content.Children.Add(buttons);

        var dialog = new ContentDialog
        {
            Title = "Texto extraído (OCR)",
            Content = content,
            CloseButtonText = "Cerrar",
            XamlRoot = Root(),
        };
        void OnCopy(object s, Microsoft.UI.Xaml.RoutedEventArgs e) { choice = OcrAction.Copy; dialog.Hide(); }
        void OnComment(object s, Microsoft.UI.Xaml.RoutedEventArgs e) { choice = OcrAction.SaveComment; dialog.Hide(); }
        void OnTxt(object s, Microsoft.UI.Xaml.RoutedEventArgs e) { choice = OcrAction.SaveTxt; dialog.Hide(); }

        copyButton.Click += OnCopy;
        commentButton.Click += OnComment;
        txtButton.Click += OnTxt;
        try
        {
            await dialog.ShowAsync();
            return choice;
        }
        finally
        {
            copyButton.Click -= OnCopy;
            commentButton.Click -= OnComment;
            txtButton.Click -= OnTxt;
        }
    }

    /// <inheritdoc/>
    public async Task ShowMessageAsync(string title, string message)
    {
        EnsureInitialized();
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "Aceptar",
            XamlRoot = Root(),
        };
        await dialog.ShowAsync();
    }

    private void EnsureInitialized()
    {
        if (_window is null)
        {
            throw new InvalidOperationException("DialogService no inicializado.");
        }
    }

    /// <summary>Resuelve el XamlRoot en el momento de abrir (la ventana ya está activada).</summary>
    private XamlRoot Root()
    {
        EnsureInitialized();
        var root = _window!.Content?.XamlRoot;
        if (root is null)
        {
            throw new InvalidOperationException("La ventana aún no está lista para mostrar diálogos.");
        }

        return root;
    }
}
