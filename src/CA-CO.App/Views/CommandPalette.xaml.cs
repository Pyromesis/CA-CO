using System.Collections.ObjectModel;
using CaCo.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CaCo.App.Views;

/// <summary>
/// Paleta de comandos (Ctrl+K): acceso rápido a cualquier acción de la app
/// escribiendo su nombre. Overlay ligero con filtrado y navegación por teclado.
/// </summary>
public sealed partial class CommandPalette : UserControl
{
    private readonly ObservableCollection<PaletteCommand> _results = [];
    private List<PaletteCommand> _commands = [];

    /// <summary>Crea la paleta.</summary>
    public CommandPalette()
    {
        InitializeComponent();
        ResultsList.ItemsSource = _results;
    }

    /// <summary>Si la paleta está abierta.</summary>
    public bool IsOpen => PalettePopup.IsOpen;

    /// <summary>Registra los comandos disponibles (desde el shell, en el arranque).</summary>
    public void RegisterCommands(IEnumerable<PaletteCommand> commands)
        => _commands = commands.ToList();

    /// <summary>Abre la paleta centrada y lista para escribir.</summary>
    public void Open()
    {
        UpdatePosition();
        QueryBox.Text = string.Empty;
        Refresh(string.Empty);
        PalettePopup.IsOpen = true;
        QueryBox.Focus(FocusState.Programmatic);
    }

    /// <summary>Cierra la paleta.</summary>
    public void Close() => PalettePopup.IsOpen = false;

    private void UpdatePosition()
    {
        var width = XamlRoot?.Size.Width ?? ActualWidth;
        PalettePopup.HorizontalOffset = Math.Max(0, (width - 560) / 2.0);
        PalettePopup.VerticalOffset = 96;
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
        => Refresh(QueryBox.Text);

    private void Refresh(string query)
    {
        var trimmed = query.Trim();
        _results.Clear();
        var matches = string.IsNullOrEmpty(trimmed)
            ? _commands
            : _commands.Where(c =>
                c.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                || c.Subtitle.Contains(trimmed, StringComparison.OrdinalIgnoreCase));

        foreach (var command in matches)
        {
            _results.Add(command);
        }

        EmptyHint.Visibility = _results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_results.Count > 0)
        {
            ResultsList.SelectedIndex = 0;
        }
    }

    private void QueryBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down:
                if (_results.Count > 0)
                {
                    ResultsList.Focus(FocusState.Programmatic);
                }
                break;
            case VirtualKey.Up:
                if (_results.Count > 0)
                {
                    ResultsList.SelectedIndex = _results.Count - 1;
                    ResultsList.Focus(FocusState.Programmatic);
                }
                break;
            case VirtualKey.Enter:
                if (ResultsList.SelectedItem is PaletteCommand command)
                {
                    Invoke(command);
                }
                break;
            case VirtualKey.Escape:
                Close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void ResultsList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void ResultsList_ItemClick(object sender, ItemClickEventArgs e)
        => Invoke((PaletteCommand)e.ClickedItem);

    private void Invoke(PaletteCommand command)
    {
        Close();
        command.Action?.Invoke();
    }
}
