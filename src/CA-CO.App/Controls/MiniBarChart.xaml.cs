using CaCo.App.Behaviors;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace CaCo.App.Controls;

/// <summary>Fila del gráfico (datos planos para enlazar desde XAML).</summary>
public sealed class BarViewModel
{
    /// <summary>Etiqueta de la categoría.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Valor absoluto.</summary>
    public int Count { get; init; }

    /// <summary>Índice para el escalonado de la animación.</summary>
    public int Index { get; init; }

    /// <summary>Fracción del máximo (0-1) para el ancho de la barra.</summary>
    public double Fraction { get; set; }

    /// <summary>Color de la barra.</summary>
    public Brush Brush { get; init; } = new SolidColorBrush(Microsoft.UI.Colors.Chocolate);

    /// <summary>Guarda si ya se animó (evita re-animar al reciclar).</summary>
    public bool AlreadyAnimated { get; set; }
}

/// <summary>Gráfico de barras horizontal pequeño y animado, en XAML puro.</summary>
public sealed partial class MiniBarChart : UserControl
{
    /// <summary>Crea el gráfico.</summary>
    public MiniBarChart()
    {
        InitializeComponent();
    }

    /// <summary>Carga las barras y lanza la animación si ya está en pantalla.</summary>
    public void SetData(IEnumerable<BarViewModel> bars)
    {
        var list = bars.ToList();
        ChartHost.ItemsSource = list;
        if (IsLoaded)
        {
            ResetAndAnimate(list);
        }
    }

    private void ResetAndAnimate(IReadOnlyList<BarViewModel> bars)
    {
        // Los contenedores ya están generados: anima directamente cada barra.
        foreach (var bar in bars)
        {
            bar.AlreadyAnimated = false;
        }

        for (var i = 0; i < ChartHost.Items.Count; i++)
        {
            if (ChartHost.ContainerFromIndex(i) is not ContentPresenter presenter)
            {
                continue;
            }

            AnimateContainer(presenter, i);
        }
    }

    private void BarItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Grid grid || grid.DataContext is not BarViewModel bar)
        {
            return;
        }

        if (bar.AlreadyAnimated)
        {
            return;
        }

        bar.AlreadyAnimated = true;
        AnimateContainer(grid, bar.Index);
    }

    private void AnimateContainer(FrameworkElement root, int index)
    {
        if (root.FindName("BarFill") is not Rectangle bar)
        {
            return;
        }

        var fraction = root.DataContext is BarViewModel vm ? vm.Fraction : 0d;
        var grow = new DoubleAnimation
        {
            From = 0d,
            To = Math.Max(0.02, fraction),
            Duration = TimeSpan.FromSeconds(0.85),
            BeginTime = TimeSpan.FromMilliseconds(index * 70),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(grow, bar);
        Storyboard.SetTargetProperty(grow, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");

        var storyboard = new Storyboard();
        storyboard.Children.Add(grow);
        storyboard.Begin();

        if (root.FindName("CountText") is TextBlock count && root.DataContext is BarViewModel vm2)
        {
            count.AnimateTo(vm2.Count, 0.9);
        }
    }
}
