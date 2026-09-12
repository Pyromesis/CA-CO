using CaCo.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace CaCo.App.Pages;

/// <summary>
/// Espacio de trabajo del documento. Solo UI: zoom, tinta vectorial y enlaces al ViewModel.
/// </summary>
public sealed partial class ReaderPage : ReaderPageBase
{
    private enum InkMode
    {
        Off,
        Pen,
        Highlighter,
    }

    private InkMode _inkMode = InkMode.Off;
    private Polyline? _currentStroke;

    /// <summary>Crea la página.</summary>
    public ReaderPage()
    {
        InitializeComponent();
    }

    private bool _pdfHardened;

    private async void PdfView_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_pdfHardened || sender is not Microsoft.UI.Xaml.Controls.WebView2 pdfView)
        {
            return;
        }

        _pdfHardened = true;
        try
        {
            await pdfView.EnsureCoreWebView2Async();
            var settings = pdfView.CoreWebView2?.Settings;
            if (settings is not null)
            {
                settings.AreDevToolsEnabled = false;
                settings.AreDefaultContextMenusEnabled = false;
                settings.IsScriptEnabled = false;
            }

            if (pdfView.CoreWebView2 is not null)
            {
                pdfView.CoreWebView2.NavigationStarting += (s, args) =>
                {
                    if (!args.Uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    {
                        args.Cancel = true;
                    }
                };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WebView2 hardening falló: {ex.Message}");
        }
    }

    private void Comments_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        var view = args.ItemContainer.ContentTemplateRoot as Views.CommentView
            ?? FindChild<Views.CommentView>(args.ItemContainer.ContentTemplateRoot as DependencyObject);
        if (view is null)
        {
            return;
        }

        if (args.InRecycleQueue || args.Item is null)
        {
            view.Note = null;
            return;
        }

        view.Note = args.Item as Note;
    }

    private static T? FindChild<T>(DependencyObject? root)
        where T : DependencyObject
    {
        if (root is null)
        {
            return null;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                return typed;
            }

            var nested = FindChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static Note? CommentOf(object sender) =>
        (sender as Button)?.DataContext as Note;

    private void CommentEdit_Click(object sender, RoutedEventArgs e)
    {
        if (CommentOf(sender) is Note note)
        {
            ViewModel.EditCommentCommand.Execute(note);
        }
    }

    private void CommentDelete_Click(object sender, RoutedEventArgs e)
    {
        if (CommentOf(sender) is Note note)
        {
            ViewModel.DeleteCommentCommand.Execute(note);
        }
    }

    private void SendSelection_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TextEditor.SelectedText))
        {
            ViewModel.AppendToComposer(TextEditor.SelectedText);
        }
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        // El PDF usa la barra del visor integrado; aquí solo imágenes.
        ImageZoom.ZoomToFactor((float)(ImageZoom.ZoomFactor + 0.25));
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        ImageZoom.ZoomToFactor((float)Math.Max(0.25, ImageZoom.ZoomFactor - 0.25));
    }

    private void PenButton_Click(object sender, RoutedEventArgs e)
    {
        _inkMode = PenButton.IsChecked == true ? InkMode.Pen : InkMode.Off;
        if (_inkMode == InkMode.Pen)
        {
            HighlighterButton.IsChecked = false;
        }
    }

    private void HighlighterButton_Click(object sender, RoutedEventArgs e)
    {
        _inkMode = HighlighterButton.IsChecked == true ? InkMode.Highlighter : InkMode.Off;
        if (_inkMode == InkMode.Highlighter)
        {
            PenButton.IsChecked = false;
        }
    }

    private void UndoStroke_Click(object sender, RoutedEventArgs e)
    {
        for (var i = InkOverlay.Children.Count - 1; i >= 0; i--)
        {
            if (InkOverlay.Children[i] is Polyline)
            {
                InkOverlay.Children.RemoveAt(i);
                break;
            }
        }
    }

    private void ClearInk_Click(object sender, RoutedEventArgs e)
    {
        for (var i = InkOverlay.Children.Count - 1; i >= 0; i--)
        {
            if (InkOverlay.Children[i] is Polyline)
            {
                InkOverlay.Children.RemoveAt(i);
            }
        }
    }

    private void InkOverlay_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_inkMode == InkMode.Off)
        {
            return;
        }

        var point = e.GetCurrentPoint(InkOverlay).Position;
        _currentStroke = new Polyline
        {
            Stroke = new SolidColorBrush(_inkMode == InkMode.Pen
                ? Color.FromArgb(255, 74, 44, 18)
                : Color.FromArgb(120, 201, 162, 39)),
            StrokeThickness = _inkMode == InkMode.Pen ? 3 : 14,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        _currentStroke.Points.Add(point);
        InkOverlay.Children.Add(_currentStroke);
        InkOverlay.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void InkOverlay_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_currentStroke is null)
        {
            return;
        }

        foreach (var point in e.GetIntermediatePoints(InkOverlay))
        {
            _currentStroke.Points.Add(point.Position);
        }

        e.Handled = true;
    }

    private void InkOverlay_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _currentStroke = null;
        InkOverlay.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private async void SaveInk_Click(object sender, RoutedEventArgs e)
    {
        const long maxPixels = 50L * 1024 * 1024;
        try
        {
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(InkGrid);
            if ((long)bitmap.PixelWidth * bitmap.PixelHeight > maxPixels)
            {
                System.Diagnostics.Debug.WriteLine("SaveInk: bitmap demasiado grande, se omite.");
                ViewModel.ReportError("La anotación es demasiado grande para guardarla (máx. 50 MP).");
                return;
            }

            var pixels = await bitmap.GetPixelsAsync();
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96,
                pixels.ToArray());
            await encoder.FlushAsync();

            if (stream.Size > 50L * 1024 * 1024)
            {
                System.Diagnostics.Debug.WriteLine("SaveInk: PNG demasiado grande, se omite.");
                ViewModel.ReportError("La anotación es demasiado grande para guardarla (máx. 50 MB).");
                return;
            }

            var bytes = new byte[stream.Size];
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);

            await ViewModel.SaveAnnotatedImageAsync(bytes, CancellationToken.None);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveInk falló (render): {ex.Message}");
            ViewModel.ReportError("No se pudo procesar la anotación para guardarla.");
        }
    }
}
