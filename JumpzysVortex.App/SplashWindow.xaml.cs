using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace JumpzysVortex.App;

public partial class SplashWindow : Window
{
    // ── Loading steps shown during init ───────────────────
    private static readonly (string Main, string Sub, double Pct)[] Steps =
    {
        ("Initialising core services...",      "Performance monitor · State engine",          8),
        ("Loading configuration...",           "Reading settings.json",                       16),
        ("Probing hardware counters...",        "CPU · RAM · GPU Engine counter",              26),
        ("Detecting temperature sources...",   "ACPI · LibreHardwareMonitor · OpenHWM",       36),
        ("Starting network monitor...",        "ICMP ping · jitter · packet loss",            47),
        ("Registering global hotkeys...",      "CTRL+SHIFT+B/R/O/D",                          56),
        ("Loading ML model...",                "FastTree binary classifier",                   65),
        ("Scanning for active games...",       "Checking 60+ known titles",                    75),
        ("Starting overlays...",               "FPS overlay · Network overlay",                84),
        ("Finalising...",                      "Building system tray · Starting monitors",     96),
        ("Ready.",                             "Jumpzys Vortex v2.2 initialised",             100),
    };

    private readonly TaskCompletionSource<bool> _done = new();

    /// <summary>Awaitable — resolves when the splash has finished its sequence.</summary>
    public Task Completion => _done.Task;

    // ── Track bar width ───────────────────────────────────
    private const double BarMaxWidth = 416; // 520 - 52*2

    public SplashWindow()
    {
        InitializeComponent();
        ContentRendered += OnRendered;
    }

    private void OnRendered(object? s, EventArgs e)
    {
        ContentRendered -= OnRendered;

        // Start visual animations
        ((Storyboard)Resources["FadeIn"]).Begin(this, true);
        ((Storyboard)Resources["GlowPulse"]).Begin(this, true);
        ((Storyboard)Resources["DotBlink"]).Begin(this, true);
        ((Storyboard)Resources["ScanAnim"]).Begin(this, true);

        // Run loading sequence on background thread
        Task.Run(RunSequence);
    }

    private async Task RunSequence()
    {
        // Each step has a small random delay to feel like real work
        var rng = new Random();

        foreach (var (main, sub, pct) in Steps)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                StatusLine.Text    = main;
                SubStatusLine.Text = sub;
                PctLabel.Text      = $"{(int)pct}%";
                AnimateBar(pct);
            });

            // Delay: 120–280ms per step — fast enough to feel snappy, slow enough to read
            await Task.Delay(rng.Next(120, 280));
        }

        // Hold "Ready" for 400ms so the user can see it
        await Task.Delay(400);

        // Fade out
        await Dispatcher.InvokeAsync(() =>
        {
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fade.Completed += (_, _) =>
            {
                _done.TrySetResult(true);
                Close();
            };
            RootBorder.BeginAnimation(OpacityProperty, fade);
        });
    }

    private void AnimateBar(double pct)
    {
        double targetWidth = BarMaxWidth * pct / 100.0;
        var anim = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ProgressFill.BeginAnimation(WidthProperty, anim);
    }
}
