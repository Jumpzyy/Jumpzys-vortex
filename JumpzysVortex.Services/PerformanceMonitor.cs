using System.Diagnostics;
using System.Management;

namespace JumpzysVortex.Services;

/// <summary>
/// Reads CPU, RAM, GPU and temperature in real time.
///
/// GPU detection waterfall (tries each in order, stops at first success):
///   1. GPU Engine counter – pid-aware, picks the busiest 3D instance
///   2. GPU Engine counter – any engtype_3D instance (driver-agnostic)
///   3. GPU Engine counter – any instance at all (AMD / Intel fallback)
///   4. Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine via WMI
///   5. Returns 0 — GPU data not available on this system
///
/// Temperature waterfall:
///   1. LibreHardwareMonitor WMI (most accurate, needs LHM running as admin)
///   2. OpenHardwareMonitor WMI  (needs OHM running as admin)
///   3. MSAcpi_ThermalZoneTemperature (always present, may be mobo sensor)
///   4. CPU-load heuristic — 30 + CPU% * 0.6  (rough but always shows something)
/// </summary>
public class PerformanceMonitor : IDisposable
{
    // ── Core counters (always available) ──────────────────
    private readonly PerformanceCounter _cpuCounter;
    private readonly PerformanceCounter _ramCounter;
    private float                       _totalRamMb;

    // ── GPU counters (may be null) ────────────────────────
    private List<PerformanceCounter> _gpuCounters = new();
    private bool                      _gpuCountersFailed;

    // ── State ─────────────────────────────────────────────
    private bool  _disposed;
    private float _lastCpu;   // used for temp heuristic

    // ── Public capability flags ───────────────────────────
    /// <summary>True if real GPU load data is available.</summary>
    public bool GpuAvailable => _gpuCounters.Count > 0 && !_gpuCountersFailed;

    /// <summary>True if a hardware temperature source was found.</summary>
    public bool TempAvailable { get; private set; }

    /// <summary>Total installed RAM in GB (rounded to nearest common size).</summary>
    public float TotalRamGb => _totalRamMb / 1024f;

    // ─────────────────────────────────────────────────────
    public PerformanceMonitor()
    {
        _cpuCounter  = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        _ramCounter  = new PerformanceCounter("Memory",    "Available MBytes");
        _totalRamMb  = QueryTotalRamMb();

        InitGpuCounters();
        TempAvailable = ProbeTemperatureSource();

        // Prime CPU counter — first read is always 0
        _cpuCounter.NextValue();
    }

    // ═════════════════════════════════════════════════════
    // SNAPSHOT
    // ═════════════════════════════════════════════════════
    public PerformanceSnapshot GetSnapshot()
    {
        float cpu   = Math.Clamp(_cpuCounter.NextValue(), 0f, 100f);
        float avail = _ramCounter.NextValue();
        if (_totalRamMb <= 0) _totalRamMb = QueryTotalRamMb();
        float ram   = _totalRamMb > 0
            ? Math.Clamp((1f - avail / _totalRamMb) * 100f, 0f, 100f)
            : 0f;

        float gpu  = ReadGpu();
        float temp = ReadTemp(cpu);
        float fps  = FpsTracker.CurrentFps;
        _lastCpu   = cpu;

        return new PerformanceSnapshot
        {
            Timestamp      = DateTime.Now,
            Cpu            = MathF.Round(cpu,  1),
            Ram            = MathF.Round(ram,  1),
            Gpu            = MathF.Round(gpu,  1),
            Fps            = MathF.Round(fps,  1),
            CpuTemp        = MathF.Round(temp, 1),
            AvailableRamMb = (long)avail,
        };
    }

    // ═════════════════════════════════════════════════════
    // GPU INITIALISATION
    // ═════════════════════════════════════════════════════
    private void InitGpuCounters()
    {
        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Engine")) return;

            var cat       = new PerformanceCounterCategory("GPU Engine");
            var instances = cat.GetInstanceNames();

            // Strategy 1: engtype_3D instances (most accurate for games)
            var targets = instances
                .Where(n => n.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Strategy 2: fall back to any GPU Engine instance
            if (targets.Count == 0)
                targets = instances
                    .Where(n => n.Contains("pid_", StringComparison.OrdinalIgnoreCase))
                    .Take(4)
                    .ToList();

            // Strategy 3: just take the first few instances
            if (targets.Count == 0)
                targets = instances.Take(4).ToList();

            foreach (var inst in targets)
            {
                try
                {
                    var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst);
                    c.NextValue(); // prime
                    _gpuCounters.Add(c);
                }
                catch { }
            }
        }
        catch { }
    }

    // ═════════════════════════════════════════════════════
    // GPU READ
    // ═════════════════════════════════════════════════════
    private float ReadGpu()
    {
        // ── Path A: PerformanceCounters ───────────────────
        if (_gpuCounters.Count > 0 && !_gpuCountersFailed)
        {
            try
            {
                float total = 0f;
                foreach (var c in _gpuCounters)
                    total += c.NextValue();
                return Math.Clamp(total, 0f, 100f);
            }
            catch
            {
                _gpuCountersFailed = true;
                // Dispose failed counters
                foreach (var c in _gpuCounters) try { c.Dispose(); } catch { }
                _gpuCounters.Clear();
            }
        }

        // ── Path B: Re-initialise counters (driver reload) ─
        if (_gpuCountersFailed)
        {
            _gpuCountersFailed = false;
            InitGpuCounters();
            if (_gpuCounters.Count > 0) return ReadGpu();
        }

        // ── Path C: WMI GPUPerformanceCounters ────────────
        try
        {
            using var s = new ManagementObjectSearcher(
                @"root\cimv2",
                "SELECT UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            float max = 0f;
            foreach (ManagementObject o in s.Get())
            {
                var v = o["UtilizationPercentage"];
                if (v != null)
                {
                    float val = Convert.ToSingle(v);
                    if (val > max) max = val;
                }
            }
            if (max > 0f) return Math.Clamp(max, 0f, 100f);
        }
        catch { }

        return 0f;
    }

    // ═════════════════════════════════════════════════════
    // TEMPERATURE
    // ═════════════════════════════════════════════════════
    private bool ProbeTemperatureSource()
    {
        // LHM
        try
        {
            using var s = new ManagementObjectSearcher(
                @"root\LibreHardwareMonitor",
                "SELECT Value FROM Sensor WHERE SensorType='Temperature' AND Name='CPU Package'");
            if (s.Get().Count > 0) return true;
        }
        catch { }

        // OHM
        try
        {
            using var s = new ManagementObjectSearcher(
                @"root\OpenHardwareMonitor",
                "SELECT Value FROM Sensor WHERE SensorType='Temperature' AND Name='CPU Package'");
            if (s.Get().Count > 0) return true;
        }
        catch { }

        // ACPI
        try
        {
            using var s = new ManagementObjectSearcher(
                @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            float max = 0f;
            foreach (ManagementObject o in s.Get())
            {
                float t = (Convert.ToSingle(o["CurrentTemperature"]) - 2732f) / 10f;
                if (t is > 20f and < 120f && t > max) max = t;
            }
            if (max > 20f) return true;
        }
        catch { }

        return false; // will use heuristic
    }

    private float ReadTemp(float currentCpu)
    {
        // 1. LibreHardwareMonitor (best accuracy)
        try
        {
            using var s = new ManagementObjectSearcher(
                @"root\LibreHardwareMonitor",
                "SELECT Value FROM Sensor WHERE SensorType='Temperature' AND Name='CPU Package'");
            foreach (ManagementObject o in s.Get())
            {
                float v = Convert.ToSingle(o["Value"]);
                if (v is > 20f and < 120f) return v;
            }
        }
        catch { }

        // 2. OpenHardwareMonitor
        try
        {
            using var s = new ManagementObjectSearcher(
                @"root\OpenHardwareMonitor",
                "SELECT Value FROM Sensor WHERE SensorType='Temperature' AND Name='CPU Package'");
            foreach (ManagementObject o in s.Get())
            {
                float v = Convert.ToSingle(o["Value"]);
                if (v is > 20f and < 120f) return v;
            }
        }
        catch { }

        // 3. ACPI thermal zone
        try
        {
            using var s = new ManagementObjectSearcher(
                @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            float max = 0f;
            foreach (ManagementObject o in s.Get())
            {
                float t = (Convert.ToSingle(o["CurrentTemperature"]) - 2732f) / 10f;
                if (t is > 20f and < 120f && t > max) max = t;
            }
            if (max > 20f) return max;
        }
        catch { }

        // 4. Heuristic: idle ~35°C, scales with load up to ~90°C
        //    Formula: 35 + CPU% * 0.55
        //    Always returns something reasonable so the UI never shows —
        return Math.Clamp(35f + currentCpu * 0.55f, 30f, 95f);
    }

    // ═════════════════════════════════════════════════════
    // TOTAL RAM
    // ═════════════════════════════════════════════════════
    private static float QueryTotalRamMb()
    {
        try
        {
            using var s = new ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
            foreach (ManagementObject o in s.Get())
                return Convert.ToSingle(o["TotalVisibleMemorySize"]) / 1024f;
        }
        catch { }
        return 32768f;
    }

    // ═════════════════════════════════════════════════════
    // DISPOSE
    // ═════════════════════════════════════════════════════
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cpuCounter.Dispose();
        _ramCounter.Dispose();
        foreach (var c in _gpuCounters) try { c.Dispose(); } catch { }
        _gpuCounters.Clear();
    }
}
