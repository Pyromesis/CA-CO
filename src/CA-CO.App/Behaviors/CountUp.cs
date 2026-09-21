using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace CaCo.App.Behaviors;

/// <summary>
/// Puente animable para números: como <see cref="TextBlock.Text"/> no es
/// animable, esta propiedad adjunta (un <see cref="double"/>) sí lo es y su
/// callback escribe el texto con formato.
/// </summary>
public static class CountUp
{
    /// <summary>Valor numérico actual (animable).</summary>
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.RegisterAttached(
            "Value",
            typeof(double),
            typeof(CountUp),
            new PropertyMetadata(0d, OnValueChanged));

    /// <summary>Lee el valor.</summary>
    public static double GetValue(DependencyObject element) => (double)element.GetValue(ValueProperty);

    /// <summary>Escribe el valor.</summary>
    public static void SetValue(DependencyObject element, double value) => element.SetValue(ValueProperty, value);

    private static void OnValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBlock textBlock)
        {
            textBlock.Text = ((double)e.NewValue).ToString("N0");
        }
    }

    /// <summary>Anima el número de 0 a <paramref name="to" /> con suavizado.</summary>
    public static void AnimateTo(this TextBlock textBlock, double to, double seconds = 1.2)
    {
        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            From = 0d,
            To = to,
            Duration = TimeSpan.FromSeconds(seconds),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, textBlock);
        // En C# el path de una propiedad adjunta debe llevar el namespace completo.
        Storyboard.SetTargetProperty(animation, "(using:CaCo.App.Behaviors.CountUp.Value)");
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
}
