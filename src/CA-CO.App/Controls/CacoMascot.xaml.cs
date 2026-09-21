using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace CaCo.App.Controls;

/// <summary>Estados de ánimo de la mascota.</summary>
public enum MascotMood
{
    /// <summary>Inactivo: flota y respira despacio.</summary>
    Idle,

    /// <summary>Alegre: rebota y sonríe (acciones con éxito).</summary>
    Happy,

    /// <summary>Concentrado: se balancea (tareas en curso).</summary>
    Working,
}

/// <summary>
/// Mascota de CA-CO: un granito de café animado en XAML puro (sin assets
/// externos). Respira, parpadea, flota y reacciona con un bocadillo.
/// </summary>
public sealed partial class CacoMascot : UserControl
{
    /// <summary>DP del ánimo actual.</summary>
    public static readonly DependencyProperty MoodProperty =
        DependencyProperty.Register(
            nameof(Mood),
            typeof(MascotMood),
            typeof(CacoMascot),
            new PropertyMetadata(MascotMood.Idle, OnMoodChanged));

    /// <summary>DP del texto del bocadillo (vacío lo oculta).</summary>
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(
            nameof(Message),
            typeof(string),
            typeof(CacoMascot),
            new PropertyMetadata(string.Empty, OnMessageChanged));

    /// <summary>Ánimo actual de la mascota.</summary>
    public MascotMood Mood
    {
        get => (MascotMood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    /// <summary>Texto del bocadillo. Vacío o nulo lo oculta.</summary>
    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    private bool _started;

    /// <summary>Crea la mascota.</summary>
    public CacoMascot()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        BreatheStoryboard.Begin();
        BlinkStoryboard.Begin();
        IdleBounceStoryboard.Begin();
        UpdateMood();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Los Storyboard "Forever" seguirían corriendo si no se paran explícitamente.
        BreatheStoryboard.Stop();
        BlinkStoryboard.Stop();
        IdleBounceStoryboard.Stop();
        HappyBounceStoryboard.Stop();
        WobbleStoryboard.Stop();
        _started = false;
    }

    private static void OnMoodChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((CacoMascot)d).UpdateMood();

    private void UpdateMood()
    {
        if (!_started)
        {
            return;
        }

        IdleBounceStoryboard.Stop();
        HappyBounceStoryboard.Stop();
        WobbleStoryboard.Stop();

        MouthFlat.Visibility = Visibility.Collapsed;
        MouthSmile.Visibility = Visibility.Collapsed;
        MouthFocus.Visibility = Visibility.Collapsed;

        switch (Mood)
        {
            case MascotMood.Happy:
                MouthSmile.Visibility = Visibility.Visible;
                CheeksInStoryboard.Begin();
                HappyBounceStoryboard.Begin();
                break;
            case MascotMood.Working:
                MouthFocus.Visibility = Visibility.Visible;
                WobbleStoryboard.Begin();
                break;
            default:
                MouthFlat.Visibility = Visibility.Visible;
                IdleBounceStoryboard.Begin();
                break;
        }
    }

    private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var mascot = (CacoMascot)d;
        var message = e.NewValue as string;

        if (string.IsNullOrWhiteSpace(message))
        {
            mascot.BubbleGrid.Visibility = Visibility.Collapsed;
            mascot.BubbleGrid.Opacity = 0;
            return;
        }

        mascot.BubbleText.Text = message;
        mascot.BubbleGrid.Visibility = Visibility.Visible;
        mascot.BubbleInStoryboard.Begin();
    }
}
