using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace CaCo.App.Extensions;

/// <summary>
/// Efecto de hover sutil con Composition: los botones crecen un poco al pasar
/// el ratón y se encogen al pulsarlos. Solo APIs integradas (sin NuGet).
/// </summary>
public static class ButtonHoverExtensions
{
    /// <summary>Activa el escalado en hover/pulse para un botón.</summary>
    public static void EnableHoverScale(this Button button, float hoverScale = 1.06f, float pressScale = 0.96f)
    {
        // Lambda no estática: captura hoverScale/pressScale (locales, sin "this").
        button.Loaded += (sender, _) =>
        {
            var b = (Button)sender;
            if (b.XamlRoot is null || ElementCompositionPreview.GetElementVisual(b) is not { } visual)
            {
                return;
            }

            var compositor = visual.Compositor;

            // Sin centrar el origen, la escala crece desde la esquina superior izquierda.
            UpdateCenter(b, visual);
            b.SizeChanged += static (sender2, _) =>
            {
                if (sender2 is Button btn && ElementCompositionPreview.GetElementVisual(btn) is { } v)
                {
                    UpdateCenter(btn, v);
                }
            };

            b.PointerEntered += (sender2, _) => Animate((Button)sender2, hoverScale, 0.18);
            b.PointerExited += (sender2, _) =>
            {
                if (!((Button)sender2).IsPressed)
                {
                    Animate((Button)sender2, 1f, 0.25);
                }
            };
            b.PointerPressed += (sender2, _) => Animate((Button)sender2, pressScale, 0.12);
            b.PointerReleased += (sender2, _) =>
            {
                var btn = (Button)sender2;
                Animate(btn, btn.IsPointerOver ? hoverScale : 1f, 0.2);
            };

            void Animate(Button target, float scale, double seconds)
            {
                var animation = compositor.CreateVector3KeyFrameAnimation();
                animation.InsertKeyFrame(1f, new Vector3(scale, scale, 1f));
                animation.Duration = TimeSpan.FromSeconds(seconds);
                animation.StopBehavior = AnimationStopBehavior.SetToFinalValue;
                ElementCompositionPreview.GetElementVisual(target).StartAnimation("Scale", animation);
            }
        };
    }

    /// <summary>Activa el efecto en varios botones a la vez.</summary>
    public static void EnableHoverScale(this IEnumerable<Button> buttons)
    {
        foreach (var button in buttons)
        {
            button.EnableHoverScale();
        }
    }

    private static void UpdateCenter(FrameworkElement element, Visual visual) =>
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2f, (float)element.ActualHeight / 2f, 0f);
}
