using System.ComponentModel;
using CaCo.App.Behaviors;
using CaCo.App.Controls;
using CaCo.App.Extensions;
using CaCo.App.ViewModels;
using CaCo.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CaCo.App.Pages;

/// <summary>Página de inicio: mascota reactiva, contadores animados y gráfico.</summary>
public sealed partial class HomePage : HomePageBase
{
    private static readonly SolidColorBrush AccentBarBrush = new(Color.FromArgb(255, 138, 90, 43));
    private static readonly SolidColorBrush NotebooksBarBrush = new(Color.FromArgb(255, 110, 162, 135));
    private static readonly SolidColorBrush FavoritesBarBrush = new(Color.FromArgb(255, 201, 160, 99));
    private static readonly SolidColorBrush TrashBarBrush = new(Color.FromArgb(255, 120, 120, 120));

    /// <summary>Crea la página.</summary>
    public HomePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // La mascota y los contadores reaccionan a los cambios del ViewModel.
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateMascot(message: null);
        AnimateCounters();
        UpdateChart();

        // Efecto hover en los botones destacados del hero.
        ImportButton.EnableHoverScale();
        CreateNotebookButton.EnableHoverScale();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HomeViewModel.DocumentCount):
            case nameof(HomeViewModel.NotebookCount):
            case nameof(HomeViewModel.FavoriteCount):
                AnimateCounters();
                UpdateChart();
                break;
            case nameof(HomeViewModel.IsBusy):
                UpdateMascot(message: ViewModel.IsBusy ? "Trabajando…" : null);
                break;
            case nameof(HomeViewModel.HasInfo):
            case nameof(HomeViewModel.HasError):
                UpdateMascot(message: null);
                break;
        }
    }

    /// <summary>Cuenta los números de 0 a su valor final.</summary>
    private void AnimateCounters()
    {
        DocumentCountText.AnimateTo(ViewModel.DocumentCount);
        NotebookCountText.AnimateTo(ViewModel.NotebookCount);
        FavoriteCountText.AnimateTo(ViewModel.FavoriteCount);
    }

    /// <summary>Ánimo de la mascota según el estado y mensaje del momento.</summary>
    private void UpdateMascot(string? message)
    {
        Mascot.Mood = ViewModel.IsBusy
            ? MascotMood.Working
            : ViewModel.HasInfo
                ? MascotMood.Happy
                : MascotMood.Idle;

        var text = message ?? ViewModel.InfoMessage;
        if (ViewModel.HasError)
        {
            text = "Algo salió mal. Revisa el mensaje de arriba.";
            Mascot.Mood = MascotMood.Working;
        }
        else if (string.IsNullOrEmpty(text) && !ViewModel.IsBusy)
        {
            text = ViewModel.IsEmpty
                ? "¡Importa tu primer documento para empezar!"
                : "¡Hola! Pulsa Ctrl+K para cualquier acción.";
        }

        Mascot.Message = text ?? string.Empty;
    }

    /// <summary>Dibuja el gráfico con las cifras reales de la biblioteca.</summary>
    private void UpdateChart()
    {
        var bars = new BarViewModel[]
        {
            new() { Label = "Documentos", Count = ViewModel.DocumentCount, Index = 0, Brush = AccentBarBrush },
            new() { Label = "Cuadernos", Count = ViewModel.NotebookCount, Index = 1, Brush = NotebooksBarBrush },
            new() { Label = "Favoritos", Count = ViewModel.FavoriteCount, Index = 2, Brush = FavoritesBarBrush },
            new() { Label = "Papelera", Count = ViewModel.TrashCount, Index = 3, Brush = TrashBarBrush },
        };

        var max = bars.Max(b => b.Count);
        foreach (var bar in bars)
        {
            bar.Fraction = max > 0 ? bar.Count / (double)max : 0d;
        }

        TypeChart.SetData(bars);
    }

    private void List_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args) =>
        DocumentListHelper.AttachRecent(args);

    private void List_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Document document)
        {
            ViewModel.OpenDetailsCommand.Execute(document);
        }
    }
}
