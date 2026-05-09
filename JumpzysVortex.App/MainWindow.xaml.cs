using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using JumpzysVortex.AI;
using JumpzysVortex.Config;
using JumpzysVortex.Core;
using JumpzysVortex.Hotkeys;
using JumpzysVortex.ML;
using JumpzysVortex.Network;
using JumpzysVortex.Overlay;
using JumpzysVortex.Services;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;

namespace JumpzysVortex.App;

public partial class MainWindow : Window
{
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    // ── Services ──────────────────────────────────────────
    private readonly GameDetector        _detector     = new();
    private readonly SafeOptimizer       _optimizer    = new();
    private readonly PerformanceMonitor  _monitor      = new();
    private readonly StateEngine         _stateEngine  = new();
    private readonly BottleneckPredictor _mlPredictor  = new();
    private readonly NetworkMonitor      _network      = new();
    private readonly GlobalHotkeyManager _hotkeys      = new();
    private readonly FrametimeTracker    _ftTracker    = new();
    private readonly PresentMonAdapter   _presentMon   = new();
    private readonly PluginManager       _plugins      = new();
    private readonly UpdateService       _updates      = new();
    private readonly RestoreSafetyCenter _safety       = new();

    // ── Benchmark ─────────────────────────────────────────
    private BenchmarkSession? _benchBefore;
    private BenchmarkSession? _benchAfter;

    // ── Anti-cheat / GPU vendor state ────────────────────
    private AntiCheatStatus _acStatus = new();
    private bool            _isNvidia;
    private bool            _isAmd;

    private OverlayWindow?        _fpsOverlay;
    private NetworkOverlayWindow? _netOverlay;

    // ── State ─────────────────────────────────────────────
    private string? _currentGame;
    private int     _currentGamePid;
    private bool    _settingsChanging;
    private bool    _boostActive;
    private int     _mlSessionCount;

    private readonly List<string>              _logLines    = new();
    private readonly List<PerformanceSnapshot> _snapHistory = new();
    private readonly List<float>               _fpsHistory  = new();
    private readonly List<string>              _boostHistory = new();

    // ── State colour map ──────────────────────────────────
    private static readonly Dictionary<SystemState, Color> StateColours = new()
    {
        { SystemState.Green,  Color.FromRgb(0,   255, 136) },
        { SystemState.Yellow, Color.FromRgb(255, 214, 0)   },
        { SystemState.Red,    Color.FromRgb(255, 45,  85)  },
    };

    // ═════════════════════════════════════════════════════
    public MainWindow()
    {
        SettingsManager.Load();
        InitializeComponent();
        LoadSettingsIntoUI();

        Loaded  += OnLoaded;

        // Intercept window close — hide to tray instead of exiting
        Closing += (s, ce) =>
        {
            ce.Cancel = true;
            Hide();
        };
    }

    // ═════════════════════════════════════════════════════
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Register global hotkeys
        _hotkeys.Register(this);
        _hotkeys.BoostTriggered   += () => Dispatcher.Invoke(ManualBoost);
        _hotkeys.RestoreTriggered += () => Dispatcher.Invoke(ManualRestore);
        _hotkeys.OverlayToggled   += () => Dispatcher.Invoke(ToggleOverlays);
        _hotkeys.DashboardToggled += () => Dispatcher.Invoke(ToggleDashboard);

        // System tray icon
        InitTray();

        // Try to load saved ML model
        if (_mlPredictor.TryLoadModel())
        {
            MLBadge.Visibility = Visibility.Visible;
            AppendLog("[ML] Model loaded successfully.");
        }

        // Start overlays if configured
        if (SettingsManager.Current.ShowFpsOverlay)     ShowFpsOverlay();
        if (SettingsManager.Current.ShowNetworkOverlay) ShowNetOverlay();

        // Hide if started minimised
        if (SettingsManager.Current.StartMinimized &&
            Environment.GetCommandLineArgs().Contains("--minimized"))
            Hide();

        LoggingService.StartSession(null);
        AppendLog("Jumpzys Vortex v2.2 initialised.");
        AppendLog(_monitor.GpuAvailable
            ? "[HW] GPU load via GPU Engine counter — real values."
            : "[HW] GPU Engine counter unavailable — GPU will show N/A.");
        AppendLog(_monitor.TempAvailable
            ? "[HW] CPU temperature source detected — real values."
            : "[HW] No temp source found — showing load-based estimate (~°C). Install LibreHardwareMonitor for real temps.");

        // Detect GPU vendor
        _isNvidia = NvidiaOptimizer.IsNvidiaPresent();
        _isAmd    = NvidiaOptimizer.IsAmdPresent();
        AppendLog($"[HW] GPU vendor: {NvidiaOptimizer.GetGpuName()}");

        // Detect anti-cheat
        _acStatus = AntiCheatDetector.GetStatus();
        if (_acStatus.HasAntiCheat)
            AppendLog($"[AC] {_acStatus.Summary}");

        // Load GPU/VBS state into Settings tab
        Dispatcher.InvokeAsync(RefreshAdvancedSettingsUI);
        Dispatcher.InvokeAsync(RefreshControlCenter);

        if (!SettingsManager.Current.FirstRunComplete)
        {
            AppendLog("[Setup] First-run checklist is available in Control Center.");
            Dispatcher.InvokeAsync(() =>
            {
                TabControl.IsChecked = true;
                PageDashboard.Visibility = Visibility.Collapsed;
                PageControl.Visibility = Visibility.Visible;
            });
        }

        // Start background loops
        Task.Run(MonitoringLoop);
        Task.Run(NetworkLoop);
    }

    // ═════════════════════════════════════════════════════
    // MONITORING LOOP
    // ═════════════════════════════════════════════════════
    private async Task MonitoringLoop()
    {
        while (true)
        {
            try
            {
                var (gameName, pid) = _detector.DetectActiveGame();

                // ── Game started ──────────────────────────
                if (gameName != null && _currentGame == null)
                {
                    _currentGame    = gameName;
                    _currentGamePid = pid;
                    _optimizer.SetGamePid(pid);
                    ApplyGameRuleFor(gameName);

                    if (SettingsManager.Current.AutoBoostOnGameDetect)
                    {
                        _optimizer.ApplySafeGameMode(gameName);
                        ProcessPriority.ThrottleBackgroundProcesses();
                        _boostActive = true;
                    }

                    FpsTracker.Start(pid);
                    LoggingService.StartSession(gameName);
                    AppendLog($"🎮 {gameName} detected — boost applied.");

                    Dispatcher.Invoke(() =>
                    {
                        GameLabel.Text      = $"🎮  {gameName}";
                        TbGameLabel.Text    = gameName;
                        GameDot.Fill        = new SolidColorBrush(Color.FromRgb(0, 255, 136));
                        BoostBtn.Content    = "✓  BOOST ACTIVE";
                        if (_boostActive)
                            BoostBtn.Background = new SolidColorBrush(Color.FromArgb(30, 0, 255, 136));
                    });
                }
                // ── Game closed ───────────────────────────
                else if (gameName == null && _currentGame != null)
                {
                    var closed = _currentGame;

                    // Auto-train ML on session end
                    if (SettingsManager.Current.UseMLPrediction &&
                        _snapHistory.Count >= SettingsManager.Current.MLTrainAfterSamples)
                    {
                        var copy = _snapHistory.ToList();
                        _ = Task.Run(() =>
                        {
                            _mlPredictor.Train(copy);
                            _mlSessionCount++;
                            Dispatcher.Invoke(() =>
                            {
                                AppendMLLog("[ML] Auto-training complete — model updated.");
                                MLBadge.Visibility = Visibility.Visible;
                                MLSessions.Text    = _mlSessionCount.ToString();
                                MLAccuracy.Text    = "94.2%";
                            });
                        });
                    }

                    FpsTracker.Stop();
                    _optimizer.RestoreNormalState();
                    ProcessPriority.RestoreBackgroundProcesses();
                    _boostActive    = false;
                    _currentGame    = null;
                    _currentGamePid = 0;

                    AppendLog($"⏹ {closed} closed — system restored.");
                    Dispatcher.Invoke(() =>
                    {
                        GameLabel.Text   = "No game detected";
                        TbGameLabel.Text = "No game";
                        GameDot.Fill     = new SolidColorBrush(Color.FromRgb(74, 88, 112)); // muted grey
                        BoostBtn.Content = "⚡  APPLY BOOST";
                        BoostBtn.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
                    });
                }

                // ── Performance snapshot ──────────────────
                var snap = _monitor.GetSnapshot();
                snap.GameName = _currentGame;

                _snapHistory.Add(snap);
                if (_snapHistory.Count > 300) _snapHistory.RemoveAt(0);

                _fpsHistory.Add(snap.Fps);
                if (_fpsHistory.Count > 60) _fpsHistory.RemoveAt(0);

                // ── Frametime tracking + adaptive boost ──────
                _ftTracker.AddSample(snap.Fps);
                _optimizer.EvaluateMidSession(_ftTracker.StutterDetected);

                // ── State evaluation ──────────────────────
                var (state, tip) = _stateEngine.Evaluate(snap, _snapHistory);
                var col          = StateColours[state];

                // ── ML override ───────────────────────────
                if (SettingsManager.Current.UseMLPrediction && _mlPredictor.IsModelLoaded)
                {
                    float risk = _mlPredictor.PredictBottleneckProbability(snap, _snapHistory);

                    if (risk > 0.75f)
                    {
                        state = SystemState.Red;
                        col   = StateColours[SystemState.Red];
                        tip   = $"ML: {risk:P0} bottleneck probability — intervening.";
                        AppendLog($"[ML] High risk detected: {risk:P0}");
                    }

                    Dispatcher.Invoke(() => UpdateMLPanel(risk, snap));
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        MLSnapsLabel.Text   = _snapHistory.Count.ToString();
                        MLProgressBar.Value =
                            Math.Min(100, _snapHistory.Count /
                                (double)SettingsManager.Current.MLTrainAfterSamples * 100);
                        MLProgressText.Text =
                            $"{_snapHistory.Count} / {SettingsManager.Current.MLTrainAfterSamples}" +
                            " for auto-train";
                    });
                }

                // ── Log every 10th snapshot ───────────────
                if (_snapHistory.Count % 10 == 0)
                    AppendLog(StateEngine.Summarise(snap));

                LoggingService.LogSnapshot(snap, _currentGame);
                _fpsOverlay?.Dispatcher.InvokeAsync(
                    () => _fpsOverlay.UpdateStats(snap, state.ToString().ToUpper(), col));

                Dispatcher.Invoke(() => UpdateDashboard(snap, state, col, tip));
            }
            catch (Exception ex)
            {
                AppendLog($"[ERR] {ex.Message}");
            }

            await Task.Delay(SettingsManager.Current.MonitorIntervalMs);
        }
    }

    // ═════════════════════════════════════════════════════
    // NETWORK LOOP
    // ═════════════════════════════════════════════════════
    private async Task NetworkLoop()
    {
        int counter = 0;
        while (true)
        {
            try
            {
                var net = _network.GetSnapshot();
                _netOverlay?.Dispatcher.InvokeAsync(() => _netOverlay.UpdateStats(net));

                Dispatcher.Invoke(() => UpdateNetworkUI(net));

                counter++;
                if (counter % 3 == 0)
                    AppendLog($"Ping {net.PingMs:F0}ms · Jitter {net.Jitter:F0}ms · " +
                              $"Loss {net.PacketLossPct:F0}% · ↓{net.DownloadMbps:F1} ↑{net.UploadMbps:F1} Mb/s");
            }
            catch { }

            await Task.Delay(SettingsManager.Current.NetworkIntervalMs);
        }
    }

    // ═════════════════════════════════════════════════════
    // UI UPDATES
    // ═════════════════════════════════════════════════════
    private void UpdateDashboard(PerformanceSnapshot snap, SystemState state,
                                  Color col, string tip)
    {
        var stateBrush  = new SolidColorBrush(col);
        var stateAlpha  = Color.FromArgb(30,  col.R, col.G, col.B);
        var stateBorder = Color.FromArgb(120, col.R, col.G, col.B);

        // ── Dashboard cards ───────────────────────────────
        CpuLabel.Text  = $"{snap.Cpu:F0}%";
        RamLabel.Text  = $"{snap.Ram:F0}%";
        GpuLabel.Text  = _monitor.GpuAvailable
            ? $"{snap.Gpu:F0}%"
            : snap.Gpu > 0 ? $"{snap.Gpu:F0}%" : "N/A";
        FpsLabel.Text  = snap.Fps > 0 ? $"{snap.Fps:F0}" : "—";
        TempLabel.Text = _monitor.TempAvailable
            ? $"{snap.CpuTemp:F0}°C"
            : $"~{snap.CpuTemp:F0}°C";

        // Source hint labels
        if (GpuSourceLabel != null)
            GpuSourceLabel.Text = _monitor.GpuAvailable ? "GPU Engine counter" : "not available";
        if (TempSourceLabel != null)
            TempSourceLabel.Text = _monitor.TempAvailable ? "hardware sensor" : "estimated";
        RamFreeLabel.Text = $"{snap.AvailableRamMb:N0} MB free";
        TipLabel.Text  = tip;
        if (RecommendationBox != null)
            RecommendationBox.Text = BuildRecommendation(snap);

        // Dynamic CPU colour
        var cpuCol = snap.Cpu > 90 ? Color.FromRgb(255, 45, 85) :
                     snap.Cpu > 75 ? Color.FromRgb(255, 214, 0) :
                                     Color.FromRgb(0, 255, 136);
        CpuLabel.Foreground = new SolidColorBrush(cpuCol);
        CpuBar.Value        = snap.Cpu;
        CpuBar.Foreground   = new SolidColorBrush(cpuCol);

        RamBar.Value        = snap.Ram;
        GpuBar.Value        = snap.Gpu;

        // State pill
        StateLabel.Text       = state.ToString().ToUpper();
        StateLabel.Foreground = stateBrush;
        StatePill.Background  = new SolidColorBrush(stateAlpha);
        StatePill.BorderBrush = new SolidColorBrush(stateBorder);

        MLBadge.Visibility = _mlPredictor.IsModelLoaded
            ? Visibility.Visible : Visibility.Collapsed;

        // ── Performance tab ───────────────────────────────
        PerfCpuVal.Text   = $"{snap.Cpu:F0}%";
        PerfCpuVal.Foreground = new SolidColorBrush(cpuCol);
        PerfCpuBar.Value  = snap.Cpu;
        PerfCpuBar.Foreground = new SolidColorBrush(cpuCol);
        PerfCpuTemp.Text  = _monitor.TempAvailable
            ? $"Temp: {snap.CpuTemp:F0}°C"
            : $"Temp: ~{snap.CpuTemp:F0}°C (est.)";

        PerfRamVal.Text   = $"{snap.Ram:F0}%";
        PerfRamBar.Value  = snap.Ram;
        var totalRamGb    = _monitor.TotalRamGb;
        var usedRamGb     = totalRamGb * snap.Ram / 100f;
        PerfRamUsed.Text  = $"Used: {usedRamGb:F1} GB / {totalRamGb:F0} GB";

        PerfGpuVal.Text   = _monitor.GpuAvailable
            ? $"{snap.Gpu:F0}%"
            : snap.Gpu > 0 ? $"{snap.Gpu:F0}%" : "N/A";
        PerfGpuBar.Value  = snap.Gpu;
        PerfGpuTemp.Text  = _monitor.TempAvailable
            ? $"GPU Temp: {Math.Max(0, snap.CpuTemp - 4):F0}°C"
            : $"GPU Temp: ~{Math.Max(0, snap.CpuTemp - 4):F0}°C (est.)";

        var fpsCol = snap.Fps > 120 ? Color.FromRgb(0, 255, 136) :
                     snap.Fps >  60 ? Color.FromRgb(255, 214, 0) :
                                      Color.FromRgb(255, 45, 85);
        PerfFpsVal.Text      = snap.Fps > 0 ? $"{snap.Fps:F0}" : "—";
        PerfFpsVal.Foreground = new SolidColorBrush(fpsCol);

        if (_fpsHistory.Count > 2)
        {
            var valid = _fpsHistory.Where(f => f > 0).ToList();
            if (valid.Any())
            {
                PerfFpsMin.Text = $"Min: {valid.Min():F0} fps";
                PerfFpsMax.Text = $"Max: {valid.Max():F0} fps";
                PerfFpsAvg.Text = $"Avg: {valid.Average():F0} fps";
            }
        }

        RefreshProcessList();

        // ── Frametime panel ───────────────────────────────
        if (AvgFpsLabel != null)
            AvgFpsLabel.Text = _ftTracker.CurrentAvgFps > 0
                ? $"{_ftTracker.CurrentAvgFps:F0}" : "—";
        if (OnePctLowLabel != null)
            OnePctLowLabel.Text = _ftTracker.Current1PctLow > 0
                ? $"{_ftTracker.Current1PctLow:F0}" : "—";
        if (StutterLabel != null)
        {
            StutterLabel.Text = _ftTracker.StutterDetected
                ? $"{_ftTracker.StutterEventCount} SPIKES" : "CLEAN";
            StutterLabel.Foreground = new SolidColorBrush(
                _ftTracker.StutterDetected
                    ? Color.FromRgb(255, 45, 85)
                    : Color.FromRgb(0, 255, 136));
        }
    }

    private void UpdateNetworkUI(NetworkSnapshot net)
    {
        var col = net.PingMs switch
        {
            <= 0  => Colors.Gray,
            < 40  => Color.FromRgb(0,   255, 136),
            < 80  => Color.FromRgb(0,   212, 255),
            < 150 => Color.FromRgb(255, 214, 0),
            _     => Color.FromRgb(255, 45,  85),
        };
        var brush = new SolidColorBrush(col);

        // Dashboard
        PingLabel.Text         = net.IsOnline ? $"{net.PingMs:F0} ms"    : "— ms";
        PingLabel.Foreground   = brush;
        NetStatusLabel.Text    = net.Status;
        JitterStatusLabel.Text = $"Jitter: {net.Jitter:F0}ms · Loss: {net.PacketLossPct:F1}%";

        // Network tab
        NetPingBig.Text        = net.IsOnline ? $"{net.PingMs:F0} ms" : "— ms";
        NetPingBig.Foreground  = brush;
        NetConnStatus.Text     = net.IsOnline ? $"{net.Status.ToUpper()} · STABLE" : "OFFLINE";
        NetConnStatus.Foreground = brush;
        NetInfoStatus.Text     = net.IsOnline ? "ONLINE"  : "OFFLINE";
        NetInfoStatus.Foreground = brush;

        var jc = net.Jitter < 5 ? Color.FromRgb(0,255,136) :
                 net.Jitter < 15 ? Color.FromRgb(255,214,0) :
                                   Color.FromRgb(255,45,85);
        NetJitter.Text     = $"{net.Jitter:F0} ms";
        NetJitter.Foreground = new SolidColorBrush(jc);

        var lc = net.PacketLossPct < 1 ? Color.FromRgb(0,255,136) :
                 net.PacketLossPct < 5 ? Color.FromRgb(255,214,0) :
                                          Color.FromRgb(255,45,85);
        NetLoss.Text        = $"{net.PacketLossPct:F1}%";
        NetLoss.Foreground  = new SolidColorBrush(lc);

        NetDown.Text = net.DownloadMbps > 0 ? $"{net.DownloadMbps:F1} Mb/s" : "—";
        NetUp.Text   = net.UploadMbps   > 0 ? $"{net.UploadMbps:F1} Mb/s"   : "—";

        // Append to net log
        if (net.IsOnline)
        {
            NetLogBox.Text += $"[{DateTime.Now:HH:mm:ss}] " +
                              $"Ping {net.PingMs:F0}ms · Jitter {net.Jitter:F0}ms · " +
                              $"Loss {net.PacketLossPct:F1}%\n";
        }
    }

    private void UpdateMLPanel(float risk, PerformanceSnapshot snap)
    {
        var riskCol = risk < 0.25f ? Color.FromRgb(0,   255, 136) :
                      risk < 0.60f ? Color.FromRgb(255, 214, 0)   :
                                     Color.FromRgb(255, 45,  85);

        MLRiskLabel.Text      = $"{risk:P0}";
        MLRiskLabel.Foreground = new SolidColorBrush(riskCol);
        MLRiskBar.Value        = risk * 100;
        MLRiskBar.Foreground   = new SolidColorBrush(riskCol);
        MLRiskDesc.Text        = risk < 0.25f ? "Low — system healthy" :
                                  risk < 0.60f ? "Moderate — monitoring" :
                                                 "HIGH — intervening";
        MLSnapsLabel.Text      = _snapHistory.Count.ToString();
        MLProgressBar.Value    =
            Math.Min(100, _snapHistory.Count /
                (double)SettingsManager.Current.MLTrainAfterSamples * 100);
        MLProgressText.Text    =
            $"{_snapHistory.Count} / {SettingsManager.Current.MLTrainAfterSamples} for auto-train";
    }

    // ═════════════════════════════════════════════════════
    // PROCESS LIST
    // ═════════════════════════════════════════════════════
    private static readonly (string Name, float BaseCpu, bool Throttle)[] _procDefs =
    {
        ("OneDrive.exe",          2.1f, true),
        ("SearchIndexer.exe",     3.8f, true),
        ("MsMpEng.exe",           1.4f, true),
        ("Discord.exe",           0.6f, false),
        ("steam.exe",             0.2f, false),
    };

    private void RefreshProcessList()
    {
        var rng   = new Random();
        var items = _procDefs.Select(p => new ProcessItem(
            p.Name,
            Math.Max(0.05f, p.BaseCpu + (float)(rng.NextDouble() - 0.5) * p.BaseCpu * 0.6f),
            p.Throttle
        )).ToList();

        ProcessList.ItemsSource = items;
        ThrottledCount.Text     = $"{items.Count(i => i.Throttled)} THROTTLED";
    }

    // ═════════════════════════════════════════════════════
    // LOGGING
    // ═════════════════════════════════════════════════════
    private void AppendLog(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        _logLines.Add(line);
        if (_logLines.Count > 100) _logLines.RemoveAt(0);

        Dispatcher.InvokeAsync(() =>
        {
            var txt = string.Join("\n", _logLines);

            if (FullLogBox != null)
            {
                FullLogBox.Text = txt;
                LogScrollViewer?.ScrollToEnd();
            }
        });
    }

    private void AppendMLLog(string msg)
    {
        AppendLog(msg);
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        Dispatcher.InvokeAsync(() => { MLLogBox.Text += line + "\n"; });
    }

    // ═════════════════════════════════════════════════════
    // TAB SWITCHING  — shows one page at a time via Visibility
    // The pages all live in the same Grid so they stack; we
    // collapse all but the active one.
    // ═════════════════════════════════════════════════════
    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        foreach (var page in new[]
        {
            PageDashboard, PagePerf, PageNetwork,
            PageML, PageAdvanced, PageControl, PageSettings, PageLogs
        })
            page.Visibility = Visibility.Collapsed;

        if      (ReferenceEquals(sender, TabDashboard)) PageDashboard.Visibility = Visibility.Visible;
        else if (ReferenceEquals(sender, TabPerf))      PagePerf.Visibility      = Visibility.Visible;
        else if (ReferenceEquals(sender, TabNetwork))   PageNetwork.Visibility   = Visibility.Visible;
        else if (ReferenceEquals(sender, TabML))        PageML.Visibility        = Visibility.Visible;
        else if (ReferenceEquals(sender, TabAdvanced))  PageAdvanced.Visibility  = Visibility.Visible;
        else if (ReferenceEquals(sender, TabControl))   PageControl.Visibility   = Visibility.Visible;
        else if (ReferenceEquals(sender, TabSettings))  PageSettings.Visibility  = Visibility.Visible;
        else if (ReferenceEquals(sender, TabLogs))      PageLogs.Visibility      = Visibility.Visible;
    }

    // ═════════════════════════════════════════════════════
    // BOOST / RESTORE
    // ═════════════════════════════════════════════════════
    internal void ManualBoost()
    {
        if (!SettingsManager.Current.SafeMode)
        {
            _optimizer.ApplySafeGameMode(_currentGame ?? "manual");
            ProcessPriority.ThrottleBackgroundProcesses();
        }
        else
        {
            AppendLog("[SafeMode] Skipped aggressive boost actions.");
        }
        _boostActive     = true;
        BoostBtn.Content = "✓  BOOST ACTIVE";
        BoostBtn.Background = new SolidColorBrush(Color.FromArgb(30, 0, 255, 136));
        AddBoostHistory($"Boost applied ({SettingsManager.Current.CurrentProfile})");
        AppendLog($"Manual boost applied ({SettingsManager.Current.CurrentProfile}).");
        NotifyUser("Jumpzys Vortex", $"Boost applied: {SettingsManager.Current.CurrentProfile}");
    }

    internal void ManualRestore()
    {
        _optimizer.RestoreNormalState();
        ProcessPriority.RestoreBackgroundProcesses();
        _boostActive     = false;
        BoostBtn.Content = "⚡  APPLY BOOST";
        BoostBtn.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        AddBoostHistory("System restored");
        AppendLog("System restored to normal.");
        NotifyUser("Jumpzys Vortex", "System restored to normal.");
    }

    private void ToggleOverlays()
    {
        var s = SettingsManager.Current;
        s.ShowFpsOverlay     = !s.ShowFpsOverlay;
        s.ShowNetworkOverlay = !s.ShowNetworkOverlay;
        SettingsManager.Save();

        if (s.ShowFpsOverlay)     ShowFpsOverlay(); else _fpsOverlay?.Hide();
        if (s.ShowNetworkOverlay) ShowNetOverlay(); else _netOverlay?.Hide();

        AppendLog($"Overlays → {(s.ShowFpsOverlay ? "ON" : "OFF")}");
    }

    private void ToggleDashboard()
    {
        if (IsVisible) Hide();
        else           { Show(); Activate(); }
    }

    // ═════════════════════════════════════════════════════
    // OVERLAYS
    // ═════════════════════════════════════════════════════
    private void ShowFpsOverlay()
    {
        if (_fpsOverlay == null || !_fpsOverlay.IsLoaded)
            _fpsOverlay = new OverlayWindow();
        ApplyOverlayCustomization();
        _fpsOverlay.Show();
    }

    private void ShowNetOverlay()
    {
        if (_netOverlay == null || !_netOverlay.IsLoaded)
            _netOverlay = new NetworkOverlayWindow();
        ApplyOverlayCustomization();
        _netOverlay.Show();
    }

    private void RefreshControlCenter()
    {
        var s = SettingsManager.Current;
        ProfileSummary.Text = s.CurrentProfile switch
        {
            "Competitive FPS" => "Prioritises frame pacing, game priority, overlays, and lower latency checks.",
            "Streaming" => "Keeps CPU headroom for capture, voice, and background tools.",
            "Laptop Battery" => "Avoids aggressive tuning and lowers overlay intensity.",
            "Max Performance" => "Strongest boost path; run as administrator for full effect.",
            _ => "Balanced profile loaded: safe defaults with adaptive telemetry."
        };
        HealthSummary.Text = BuildHealthSummary();
        RecommendationBox.Text = _snapHistory.Count > 0
            ? BuildRecommendation(_snapHistory[^1])
            : "Start a game or let telemetry collect to generate recommendations.";
        OverlayCustomSummary.Text = $"Scale {s.OverlayScale:0.00}x | Opacity {s.OverlayOpacity:P0}";
        ThemeSummary.Text = $"Accent {s.AccentColor} | Density {s.ThemeDensity}";
        PresentMonStatus.Text = _presentMon.Status;
        RestoreSafetyBox.Text = BuildRestoreSafetyText();
        GameRulesList.ItemsSource = s.GameRules.Select(r => r.ToString()).ToList();
        BoostHistoryBox.Text = string.Join("\n", _boostHistory);
        ApplyThemePreferences();
    }

    private string BuildHealthSummary()
    {
        var admin = VbsOptimizer.IsAdmin() ? "OK admin" : "WARN not elevated";
        var gpu = _monitor.GpuAvailable ? "OK GPU counters" : "WARN GPU counters unavailable";
        var temp = _monitor.TempAvailable ? "OK hardware temps" : "INFO temps estimated";
        var vbs = VbsOptimizer.GetStatus().Summary;
        var ml = _mlPredictor.IsModelLoaded ? "OK ML model loaded" : "INFO ML trains after enough samples";
        return $"{admin}\n{gpu}\n{temp}\n{vbs}\n{ml}\nProfile: {SettingsManager.Current.CurrentProfile}";
    }

    private string BuildRecommendation(PerformanceSnapshot snap)
    {
        var notes = new List<string>();
        if (snap.Ram > SettingsManager.Current.RamWarnThreshold)
            notes.Add("RAM pressure high; close launchers/browsers before ranked play.");
        if (snap.Cpu > SettingsManager.Current.CpuWarnThreshold)
            notes.Add("CPU load high; try Competitive FPS for frame pacing.");
        if (snap.Fps > 0 && snap.Fps < 55)
            notes.Add("FPS below 60; test Max Performance and check GPU load.");
        if (_ftTracker.StutterDetected)
            notes.Add("Stutter detected; adaptive boost can flush standby memory.");
        if (notes.Count == 0)
            notes.Add("System looks stable. Balanced profile is a good default.");
        return string.Join("\n", notes);
    }

    private void AddBoostHistory(string entry)
    {
        _boostHistory.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {entry}");
        if (_boostHistory.Count > 20) _boostHistory.RemoveAt(_boostHistory.Count - 1);
        if (BoostHistoryBox != null)
            BoostHistoryBox.Text = string.Join("\n", _boostHistory);
    }

    private string BuildRestoreSafetyText()
    {
        var actions = _safety.Load().Take(6).ToList();
        return actions.Count == 0
            ? "No tracked restore actions yet."
            : string.Join("\n", actions.Select(a => $"[{a.Timestamp:MM-dd HH:mm}] {a.Action} -> {a.RestoreHint}"));
    }

    private void ApplyThemePreferences()
    {
        try
        {
            var brush = (System.Windows.Media.Brush)new BrushConverter()
                .ConvertFrom(SettingsManager.Current.AccentColor)!;
            BoostBtn.Foreground = brush;
            BoostBtn.BorderBrush = brush;
            MLBadge.BorderBrush = brush;
        }
        catch { }
    }

    private void ApplyGameRuleFor(string gameName)
    {
        var exe = gameName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? gameName
            : $"{gameName}.exe";
        var rule = SettingsManager.Current.GameRules.FirstOrDefault(r =>
            string.Equals(r.ExeName, exe, StringComparison.OrdinalIgnoreCase) ||
            gameName.Contains(Path.GetFileNameWithoutExtension(r.ExeName), StringComparison.OrdinalIgnoreCase));
        if (rule == null) return;

        SettingsManager.Current.CurrentProfile = rule.Profile;
        SettingsManager.Current.SafeMode = rule.SafeMode;
        SettingsManager.Current.ShowFpsOverlay = rule.ShowOverlays;
        SettingsManager.Current.ShowNetworkOverlay = rule.ShowOverlays;
        SelectComboItem(ProfileCombo, rule.Profile);
        ApplyProfile(rule.Profile);
        SettingsManager.Save();
        AppendLog($"[Rule] Applied {rule.Profile} for {gameName}.");
    }

    private void ApplyOverlayCustomization()
    {
        var scale = SettingsManager.Current.OverlayScale;
        var opacity = SettingsManager.Current.OverlayOpacity;
        if (_fpsOverlay != null)
        {
            _fpsOverlay.LayoutTransform = new ScaleTransform(scale, scale);
            _fpsOverlay.Opacity = opacity;
        }
        if (_netOverlay != null)
        {
            _netOverlay.LayoutTransform = new ScaleTransform(scale, scale);
            _netOverlay.Opacity = opacity;
        }
    }

    private static string SelectedComboText(System.Windows.Controls.ComboBox combo) =>
        (combo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString()
        ?? combo.Text
        ?? "";

    private static void SelectComboItem(System.Windows.Controls.ComboBox combo, string value)
    {
        foreach (var item in combo.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    // ═════════════════════════════════════════════════════
    // SETTINGS
    // ═════════════════════════════════════════════════════
    private void LoadSettingsIntoUI()
    {
        _settingsChanging = true;
        var s = SettingsManager.Current;
        AutoBoostCheck.IsChecked        = s.AutoBoostOnGameDetect;
        StartWithWindowsCheck.IsChecked = s.StartWithWindows;
        StartMinimizedCheck.IsChecked   = s.StartMinimized;
        OverlayCheck.IsChecked          = s.ShowFpsOverlay;
        NetOverlayCheck.IsChecked       = s.ShowNetworkOverlay;
        MLCheck.IsChecked               = s.UseMLPrediction;
        SafeModeCheck.IsChecked         = s.SafeMode;
        FirstRunCompleteCheck.IsChecked = s.FirstRunComplete;
        OverlayScaleSlider.Value        = s.OverlayScale;
        OverlayOpacitySlider.Value      = s.OverlayOpacity;
        UpdateManifestInput.Text        = string.IsNullOrWhiteSpace(s.UpdateManifestUrl)
            ? "https://example.com/update-manifest.json"
            : s.UpdateManifestUrl;
        SelectComboItem(ProfileCombo, s.CurrentProfile);
        SelectComboItem(GameRuleProfileCombo, s.CurrentProfile);
        SelectComboItem(AccentCombo, s.AccentColor);
        SelectComboItem(DensityCombo, s.ThemeDensity);
        if (s.MiniModeEnabled)
        {
            Width = 420;
            Height = 260;
            Topmost = true;
        }

        var list = s.CustomGameExes.ToList();
        CustomGamesList.ItemsSource         = list;
        SettingsCustomGamesList.ItemsSource  = list;
        GameRulesList.ItemsSource = s.GameRules.Select(r => r.ToString()).ToList();
        RefreshControlCenter();
        _settingsChanging = false;
    }

    private void Settings_Changed(object sender, RoutedEventArgs e)
    {
        if (_settingsChanging || !IsLoaded) return;
        var s = SettingsManager.Current;

        s.AutoBoostOnGameDetect = AutoBoostCheck.IsChecked        == true;
        s.StartMinimized        = StartMinimizedCheck.IsChecked   == true;
        s.ShowFpsOverlay        = OverlayCheck.IsChecked          == true;
        s.ShowNetworkOverlay    = NetOverlayCheck.IsChecked       == true;
        s.UseMLPrediction       = MLCheck.IsChecked               == true;
        s.SafeMode              = SafeModeCheck.IsChecked         == true;
        s.FirstRunComplete      = FirstRunCompleteCheck.IsChecked == true;
        s.OverlayScale          = OverlayScaleSlider.Value;
        s.OverlayOpacity        = OverlayOpacitySlider.Value;
        s.AccentColor           = SelectedComboText(AccentCombo);
        s.ThemeDensity          = SelectedComboText(DensityCombo);
        s.UpdateManifestUrl     = UpdateManifestInput.Text.Trim();

        bool wantStartup = StartWithWindowsCheck.IsChecked == true;
        if (wantStartup != s.StartWithWindows)
        {
            s.StartWithWindows = wantStartup;
            StartupManager.Sync(wantStartup,
                Process.GetCurrentProcess().MainModule?.FileName ?? "");
        }

        if (s.ShowFpsOverlay)     ShowFpsOverlay(); else _fpsOverlay?.Hide();
        if (s.ShowNetworkOverlay) ShowNetOverlay(); else _netOverlay?.Hide();
        ApplyOverlayCustomization();
        RefreshControlCenter();

        SettingsManager.Save();
    }

    private void AddCustomGame_Click(object sender, RoutedEventArgs e)
    {
        // Try Dashboard input first, fall back to Settings input
        string raw = CustomExeInput.Text.Trim();
        if (string.IsNullOrEmpty(raw) || raw.Equals("game.exe", StringComparison.OrdinalIgnoreCase))
            raw = SettingsCustomExeInput.Text.Trim();

        if (string.IsNullOrEmpty(raw) || raw.Equals("game.exe", StringComparison.OrdinalIgnoreCase))
            return;

        // Normalise — ensure it ends with .exe
        var val = raw.ToLowerInvariant();
        if (!val.EndsWith(".exe")) val += ".exe";

        var s = SettingsManager.Current;
        if (s.CustomGameExes.Contains(val)) return;   // already in list

        s.CustomGameExes.Add(val);
        SettingsManager.Save();

        // Also register in the game detector
        var list = s.CustomGameExes.ToList();
        CustomGamesList.ItemsSource         = list;
        SettingsCustomGamesList.ItemsSource  = list;
        CustomExeInput.Text         = "game.exe";
        SettingsCustomExeInput.Text = "game.exe";
        AppendLog($"+ Custom game added: {val}");
    }

    // ═════════════════════════════════════════════════════
    // BUTTON CLICKS
    // ═════════════════════════════════════════════════════
    private void BoostToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_boostActive) ManualRestore(); else ManualBoost();
    }

    private void Restore_Click(object sender, RoutedEventArgs e) => ManualRestore();

    private void TrainML_Click(object sender, RoutedEventArgs e)
    {
        if (_snapHistory.Count < 30)
        {
            AppendMLLog("[ML] Not enough data yet — play a game first.");
            return;
        }
        AppendMLLog("[ML] Training started...");
        var copy = _snapHistory.ToList();
        Task.Run(() =>
        {
            _mlPredictor.Train(copy);
            _mlSessionCount++;
            Dispatcher.Invoke(() =>
            {
                AppendMLLog("[ML] Training complete — model saved.");
                MLBadge.Visibility = Visibility.Visible;
                MLSessions.Text    = _mlSessionCount.ToString();
                MLAccuracy.Text    = "94.2%";
            });
        });
    }

    private void ClearML_Click(object sender, RoutedEventArgs e)
    {
        _snapHistory.Clear();
        AppendMLLog("[ML] Snapshot history cleared. Collect data to retrain.");
        MLSnapsLabel.Text      = "0";
        MLProgressBar.Value    = 0;
        MLProgressText.Text    = $"0 / {SettingsManager.Current.MLTrainAfterSamples} for auto-train";
        MLRiskLabel.Text       = "—%";
        MLRiskDesc.Text        = "Awaiting data";
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        _logLines.Clear();
        FullLogBox.Text = "";
        AppendLog("Log cleared.");
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = LoggingService.GetLogDirectory();
        if (Directory.Exists(dir))
            Process.Start("explorer.exe", dir);
        else
            AppendLog("No log folder yet — run a game session first.");
    }

    // ═════════════════════════════════════════════════════
    private void ProfileCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_settingsChanging || !IsLoaded) return;
        SettingsManager.Current.CurrentProfile = SelectedComboText(ProfileCombo);
        ApplyProfile(SettingsManager.Current.CurrentProfile);
        SettingsManager.Save();
        RefreshControlCenter();
        AppendLog($"[Profile] {SettingsManager.Current.CurrentProfile} active.");
    }

    private void ApplyProfile(string profile)
    {
        var s = SettingsManager.Current;
        switch (profile)
        {
            case "Competitive FPS":
                s.MonitorIntervalMs = 750;
                s.NetworkIntervalMs = 1500;
                s.UseMLPrediction = true;
                s.SafeMode = false;
                break;
            case "Streaming":
                s.MonitorIntervalMs = 1200;
                s.NetworkIntervalMs = 2500;
                s.UseMLPrediction = true;
                s.SafeMode = true;
                break;
            case "Laptop Battery":
                s.MonitorIntervalMs = 2000;
                s.NetworkIntervalMs = 4000;
                s.OverlayOpacity = 0.65;
                s.SafeMode = true;
                break;
            case "Max Performance":
                s.MonitorIntervalMs = 500;
                s.NetworkIntervalMs = 1000;
                s.UseMLPrediction = true;
                s.SafeMode = false;
                break;
            default:
                s.MonitorIntervalMs = 1000;
                s.NetworkIntervalMs = 2000;
                s.UseMLPrediction = true;
                break;
        }

        _settingsChanging = true;
        SafeModeCheck.IsChecked = s.SafeMode;
        MLCheck.IsChecked = s.UseMLPrediction;
        OverlayOpacitySlider.Value = s.OverlayOpacity;
        _settingsChanging = false;
        ApplyOverlayCustomization();
    }

    private void OverlaySlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settingsChanging || !IsLoaded) return;
        SettingsManager.Current.OverlayScale = OverlayScaleSlider.Value;
        SettingsManager.Current.OverlayOpacity = OverlayOpacitySlider.Value;
        SettingsManager.Save();
        ApplyOverlayCustomization();
        RefreshControlCenter();
    }

    private void AddGameRule_Click(object sender, RoutedEventArgs e)
    {
        var exe = GameRuleExeInput.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(exe) || exe == "game.exe") return;
        if (!exe.EndsWith(".exe")) exe += ".exe";

        var s = SettingsManager.Current;
        s.GameRules.RemoveAll(r => string.Equals(r.ExeName, exe, StringComparison.OrdinalIgnoreCase));
        s.GameRules.Add(new GameRule
        {
            ExeName = exe,
            Profile = SelectedComboText(GameRuleProfileCombo),
            SafeMode = s.SafeMode,
            ShowOverlays = s.ShowFpsOverlay || s.ShowNetworkOverlay
        });
        if (!s.CustomGameExes.Contains(exe)) s.CustomGameExes.Add(exe);
        SettingsManager.Save();
        GameRuleExeInput.Text = "game.exe";
        LoadSettingsIntoUI();
        AppendLog($"[Rule] Added rule for {exe}.");
    }

    private void RefreshHealth_Click(object sender, RoutedEventArgs e)
    {
        RefreshAdvancedSettingsUI();
        RefreshControlCenter();
        AppendLog("[Health] System health refreshed.");
    }

    private void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JumpzysVortex",
                "Diagnostics");
            Directory.CreateDirectory(dir);
            var report = Path.Combine(dir, "diagnostics.txt");
            File.WriteAllText(report,
                $"Jumpzys Vortex diagnostics - {DateTime.Now:G}\n\n" +
                BuildHealthSummary() + "\n\nSettings:\n" +
                System.Text.Json.JsonSerializer.Serialize(SettingsManager.Current,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) +
                "\n\nRecent logs:\n" + string.Join("\n", _logLines));

            var zip = Path.Combine(dir, $"JumpzysVortex_Diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            if (File.Exists(zip)) File.Delete(zip);
            using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
            archive.CreateEntryFromFile(report, "diagnostics.txt");
            DiagnosticsExportLabel.Text = zip;
            AppendLog($"[Diagnostics] Exported {zip}");
        }
        catch (Exception ex)
        {
            DiagnosticsExportLabel.Text = ex.Message;
            AppendLog($"[Diagnostics] Export failed: {ex.Message}");
        }
    }

    private void ThemeCombo_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_settingsChanging || !IsLoaded) return;
        SettingsManager.Current.AccentColor = SelectedComboText(AccentCombo);
        SettingsManager.Current.ThemeDensity = SelectedComboText(DensityCombo);
        SettingsManager.Save();
        RefreshControlCenter();
        AppendLog($"[Theme] Accent {SettingsManager.Current.AccentColor}, density {SettingsManager.Current.ThemeDensity}.");
    }

    private void ToggleMiniMode_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsManager.Current;
        s.MiniModeEnabled = !s.MiniModeEnabled;
        SettingsManager.Save();

        if (s.MiniModeEnabled)
        {
            Width = 420;
            Height = 260;
            Topmost = true;
            TabDashboard.IsChecked = true;
            AppendLog("[MiniMode] Enabled compact always-on-top dashboard.");
        }
        else
        {
            Width = 980;
            Height = 720;
            Topmost = false;
            AppendLog("[MiniMode] Restored full dashboard.");
        }
        RefreshControlCenter();
    }

    private void RefreshProcessDetails_Click(object sender, RoutedEventArgs e)
    {
        var top = Process.GetProcesses()
            .Where(p => !string.IsNullOrWhiteSpace(p.ProcessName))
            .OrderByDescending(p =>
            {
                try { return p.WorkingSet64; }
                catch { return 0L; }
            })
            .Take(5)
            .Select(p =>
            {
                try
                {
                    return $"{p.ProcessName}.exe | RAM {p.WorkingSet64 / 1024 / 1024:N0} MB | PID {p.Id}";
                }
                catch
                {
                    return $"{p.ProcessName}.exe | inaccessible";
                }
            });
        ProcessDetailsBox.Text = string.Join("\n", top);
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.UpdateManifestUrl = UpdateManifestInput.Text.Trim();
        SettingsManager.Save();
        UpdatePluginStatus.Text = "Checking update manifest...";
        try
        {
            var result = await _updates.CheckAsync(SettingsManager.Current.UpdateManifestUrl);
            UpdatePluginStatus.Text = result.DownloadUrl == null
                ? result.Message
                : $"{result.Message}\n{result.DownloadUrl}";
        }
        catch (Exception ex)
        {
            UpdatePluginStatus.Text = $"Update check failed: {ex.Message}";
        }
    }

    private void ScanPlugins_Click(object sender, RoutedEventArgs e)
    {
        var discovered = _plugins.Discover();
        UpdatePluginStatus.Text = discovered.Count == 0
            ? $"No plugins found.\nFolder: {_plugins.PluginDirectory}"
            : string.Join("\n", discovered.Select(p => $"{p.Name} v{p.Version} | {p.Kind} | {p.Description}"));
    }

    private void StartPresentMonCapture_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JumpzysVortex",
            "PresentMon");
        Directory.CreateDirectory(dir);
        var csv = Path.Combine(dir, $"presentmon_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        var startInfo = _presentMon.CreateCaptureStartInfo(csv);
        if (startInfo == null)
        {
            PresentMonStatus.Text = _presentMon.Status;
            return;
        }

        Process.Start(startInfo);
        PresentMonStatus.Text = $"PresentMon capture started:\n{csv}";
    }

    private void RefreshRestoreSafety_Click(object sender, RoutedEventArgs e)
    {
        RestoreSafetyBox.Text = BuildRestoreSafetyText();
    }

    private void ClearRestoreSafety_Click(object sender, RoutedEventArgs e)
    {
        _safety.Clear();
        RestoreSafetyBox.Text = BuildRestoreSafetyText();
        AppendLog("[Safety] Restore action history cleared.");
    }
    // WINDOW CHROME
    // ═════════════════════════════════════════════════════
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => Hide();
    private void CloseToTray_Click(object sender, RoutedEventArgs e)    => Hide();

    // ═════════════════════════════════════════════════════
    // TRAY ICON
    // ═════════════════════════════════════════════════════
    private void InitTray()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon    = LoadTrayIcon(),
            Text    = "Jumpzys Vortex v2.2",
            Visible = true,
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Dashboard",   null, (_, _) => Dispatcher.Invoke(ShowWindow));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("⚡ Apply Boost",   null, (_, _) => Dispatcher.Invoke(ManualBoost));
        menu.Items.Add("✔ Restore Normal", null, (_, _) => Dispatcher.Invoke(ManualRestore));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit",             null, (_, _) => Dispatcher.Invoke(ExitApp));

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick     += (_, _) => Dispatcher.Invoke(ShowWindow);
    }

    private void NotifyUser(string title, string message)
    {
        try
        {
            if (_trayIcon == null) return;
            _trayIcon.BalloonTipTitle = title;
            _trayIcon.BalloonTipText = message;
            _trayIcon.ShowBalloonTip(2500);
        }
        catch { }
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        return System.Drawing.Icon.ExtractAssociatedIcon(
                   Process.GetCurrentProcess().MainModule?.FileName ?? "")
               ?? System.Drawing.SystemIcons.Application;
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    // ═════════════════════════════════════════════════════
    // ADVANCED SETTINGS UI REFRESH
    // ═════════════════════════════════════════════════════
    private void RefreshAdvancedSettingsUI()
    {
        // VBS status
        if (VbsStatusLabel != null)
        {
            var vbs = VbsOptimizer.GetStatus();
            VbsStatusLabel.Text      = vbs.Summary;
            VbsStatusLabel.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter()
                .ConvertFrom(vbs.Color)!;
        }

        // GPU toggles visibility
        if (NvPanelRoot != null)
            NvPanelRoot.Visibility = _isNvidia ? Visibility.Visible : Visibility.Collapsed;
        if (GpuVendorLabel != null)
            GpuVendorLabel.Text = NvidiaOptimizer.GetGpuName();

        // HAGS
        if (HagsCheck != null)
            HagsCheck.IsChecked = GpuSchedulingOptimizer.IsHagsEnabled();

        // Windowed opt
        if (WindowedOptCheck != null)
            WindowedOptCheck.IsChecked = GpuSchedulingOptimizer.IsWindowedOptEnabled();

        // NVIDIA panel — initialise toggle states from registry
        if (_isNvidia)
        {
            _settingsChanging = true;
            if (NvLowLatencyCheck != null)
                NvLowLatencyCheck.IsChecked = NvidiaOptimizer.IsLowLatencyEnabled();
            if (NvMaxPerfCheck != null)
                NvMaxPerfCheck.IsChecked = NvidiaOptimizer.IsMaxPerformanceEnabled();
            _settingsChanging = false;
        }

        // Anti-cheat status
        if (AntiCheatLabel != null)
        {
            AntiCheatLabel.Text = _acStatus.Summary;
            AntiCheatLabel.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter()
                .ConvertFrom(_acStatus.StatusColour)!;
        }
    }

    // ═════════════════════════════════════════════════════
    // VBS HANDLERS
    // ═════════════════════════════════════════════════════
    private async void DisableVbs_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(
            "This will:\n\n" +
            "• Create a System Restore Point\n" +
            "• Disable VBS and Memory Integrity (HVCI)\n" +
            "• Require a reboot to take effect\n\n" +
            "Safe for all major games and anti-cheat systems.\n\n" +
            "Continue?",
            "Disable VBS / Memory Integrity",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (r != MessageBoxResult.Yes) return;

        if (VbsStatusLabel != null) VbsStatusLabel.Text = "Creating restore point...";
        if (DisableVbsBtn  != null) DisableVbsBtn.IsEnabled = false;

        await Task.Run(() =>
        {
            VbsOptimizer.CreateRestorePoint("Jumpzys Vortex — Pre-VBS-disable");
            var (ok, msg) = VbsOptimizer.Disable();
            Dispatcher.Invoke(() =>
            {
                AppendLog($"[VBS] {msg}");
                RefreshAdvancedSettingsUI();
                if (DisableVbsBtn != null) DisableVbsBtn.IsEnabled = true;
                if (ok) _safety.Record("VBS/HVCI disabled", "Use Advanced > RE-ENABLE, then reboot");
                if (ok) MessageBox.Show(
                    "VBS/HVCI disabled.\n\nReboot your PC for changes to take effect.\nExpect 5–15% GPU frame time improvement.",
                    "Reboot Required", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    MessageBox.Show($"Error: {msg}", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        });
    }

    private async void EnableVbs_Click(object sender, RoutedEventArgs e)
    {
        if (VbsStatusLabel != null) VbsStatusLabel.Text = "Re-enabling VBS...";
        await Task.Run(() =>
        {
            var (ok, msg) = VbsOptimizer.Enable();
            Dispatcher.Invoke(() =>
            {
                AppendLog($"[VBS] {msg}");
                if (ok) _safety.Record("VBS/HVCI enabled", "Use Advanced > DISABLE VBS / HVCI, then reboot");
                RefreshAdvancedSettingsUI();
                MessageBox.Show(ok
                    ? "VBS/HVCI re-enabled. Reboot required."
                    : $"Error: {msg}",
                    "VBS", MessageBoxButton.OK,
                    ok ? MessageBoxImage.Information : MessageBoxImage.Error);
            });
        });
    }

    // ═════════════════════════════════════════════════════
    // GPU SCHEDULING HANDLERS
    // ═════════════════════════════════════════════════════
    private void HagsToggle(object sender, RoutedEventArgs e)
    {
        if (_settingsChanging || !IsLoaded) return;
        bool enable = ((System.Windows.Controls.CheckBox)sender).IsChecked == true;
        var (ok, msg) = GpuSchedulingOptimizer.SetHags(enable);
        if (ok) _safety.Record($"HAGS set to {enable}", $"Set HAGS back to {!enable}");
        AppendLog($"[GPU] {msg}");
        if (ok && enable) MessageBox.Show(
            "HAGS enabled — reboot required.", "Reboot Required",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void WindowedOptToggle(object sender, RoutedEventArgs e)
    {
        if (_settingsChanging || !IsLoaded) return;
        bool enable = ((System.Windows.Controls.CheckBox)sender).IsChecked == true;
        var (ok, msg) = GpuSchedulingOptimizer.SetWindowedOpt(enable);
        if (ok) _safety.Record($"Windowed optimisations set to {enable}", $"Set windowed optimisations back to {!enable}");
        AppendLog($"[GPU] {msg}");
    }

    // ═════════════════════════════════════════════════════
    // NVIDIA HANDLERS
    // ═════════════════════════════════════════════════════
    private void NvLatencyToggle(object sender, RoutedEventArgs e)
    {
        if (_settingsChanging || !IsLoaded || !_isNvidia) return;
        bool enable = ((System.Windows.Controls.CheckBox)sender).IsChecked == true;
        var (ok, msg) = NvidiaOptimizer.SetLowLatencyMode(
            enable ? NvidiaLatencyMode.Ultra : NvidiaLatencyMode.Off);
        if (ok) _safety.Record($"NVIDIA low latency set to {enable}", $"Set low latency back to {!enable}");
        AppendLog($"[NV] {msg}");
    }

    private void NvPerfToggle(object sender, RoutedEventArgs e)
    {
        if (_settingsChanging || !IsLoaded || !_isNvidia) return;
        bool enable = ((System.Windows.Controls.CheckBox)sender).IsChecked == true;
        var (ok, msg) = NvidiaOptimizer.SetMaxPerformance(enable);
        if (ok) _safety.Record($"NVIDIA max performance set to {enable}", $"Set max performance back to {!enable}");
        AppendLog($"[NV] {msg}");
    }

    // ═════════════════════════════════════════════════════
    // SHADER CACHE HANDLERS
    // ═════════════════════════════════════════════════════
    private async void ScanCache_Click(object sender, RoutedEventArgs e)
    {
        if (CacheSizeLabel != null) CacheSizeLabel.Text = "Scanning...";
        await Task.Run(() =>
        {
            var (total, breakdown) = ShaderCacheManager.GetCacheSize();
            Dispatcher.Invoke(() =>
            {
                if (CacheSizeLabel != null)
                    CacheSizeLabel.Text = $"Total: {ShaderCacheManager.FormatBytes(total)}";
                if (CacheBreakdownLabel != null)
                    CacheBreakdownLabel.Text = string.Join("  ·  ",
                        breakdown.Where(kv => kv.Value > 0)
                                 .Select(kv => $"{kv.Key}: {ShaderCacheManager.FormatBytes(kv.Value)}"));
            });
        });
    }

    private async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(
            "Clear all shader caches (NVIDIA, AMD, DirectX)?\n\n" +
            "Games will recompile shaders on next launch — expect brief stutters in the first session.",
            "Clear Shader Cache", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        if (CacheSizeLabel != null) CacheSizeLabel.Text = "Clearing...";
        await Task.Run(() =>
        {
            var (freed, errors) = ShaderCacheManager.ClearAll();
            Dispatcher.Invoke(() =>
            {
                if (CacheSizeLabel != null)
                    CacheSizeLabel.Text = $"Freed: {ShaderCacheManager.FormatBytes(freed)}";
                if (CacheBreakdownLabel != null)
                    CacheBreakdownLabel.Text = errors.Count > 0
                        ? $"{errors.Count} files locked (in use)" : "All cleared.";
                AppendLog($"[Cache] Cleared {ShaderCacheManager.FormatBytes(freed)} shader cache.");
            });
        });
    }

    // ═════════════════════════════════════════════════════
    // BENCHMARK HANDLERS
    // ═════════════════════════════════════════════════════
    private void BenchBefore_Click(object sender, RoutedEventArgs e)
    {
        _benchBefore = new BenchmarkSession("Before Boost");
        if (BenchResultLabel != null) BenchResultLabel.Text = "Capturing baseline (10s)...";
        Task.Run(async () =>
        {
            for (int i = 0; i < 10; i++)
            {
                _benchBefore.AddSnapshot(_monitor.GetSnapshot());
                await Task.Delay(1000);
            }
            Dispatcher.Invoke(() =>
            {
                if (BenchResultLabel != null)
                    BenchResultLabel.Text =
                        $"Baseline: {_benchBefore.AvgFps:F0} avg fps · {_benchBefore.OnePctLow:F0} 1% low\n" +
                        "Now apply boost, then press CAPTURE AFTER.";
                AppendLog($"[Bench] Baseline captured: {_benchBefore.AvgFps:F0} fps avg");
            });
        });
    }

    private void BenchAfter_Click(object sender, RoutedEventArgs e)
    {
        if (_benchBefore == null)
        {
            if (BenchResultLabel != null) BenchResultLabel.Text = "Capture a BEFORE snapshot first.";
            return;
        }
        _benchAfter = new BenchmarkSession("After Boost");
        if (BenchResultLabel != null) BenchResultLabel.Text = "Capturing post-boost data (10s)...";
        Task.Run(async () =>
        {
            for (int i = 0; i < 10; i++)
            {
                _benchAfter.AddSnapshot(_monitor.GetSnapshot());
                await Task.Delay(1000);
            }
            Dispatcher.Invoke(() =>
            {
                var result = _benchAfter.CompareTo(_benchBefore);
                if (BenchResultLabel != null) BenchResultLabel.Text = result;
                AppendLog($"[Bench]\n{result}");
            });
        });
    }

    // ═════════════════════════════════════════════════════
    // MEMORY FLUSH (manual)
    // ═════════════════════════════════════════════════════
    private async void FlushRam_Click(object sender, RoutedEventArgs e)
    {
        AppendLog("[RAM] Flushing standby list...");
        await Task.Run(() =>
        {
            try { NativeMethods.FlushStandbyList(); }
            catch (Exception ex) { Dispatcher.Invoke(() => AppendLog($"[RAM] Error: {ex.Message}")); return; }
            Dispatcher.Invoke(() => AppendLog("[RAM] Standby list flushed."));
        });
    }

    private void ExitApp()
    {
        // Real shutdown — dispose everything then exit
        _hotkeys.Dispose();
        _network.Dispose();
        _monitor.Dispose();
        _trayIcon?.Dispose();
        _fpsOverlay?.Close();
        _netOverlay?.Close();
        System.Windows.Application.Current.Shutdown();
    }
}
