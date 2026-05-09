using System.Diagnostics;
using System.Net.NetworkInformation;

namespace JumpzysVortex.Network;

public class NetworkMonitor : IDisposable
{
    // ── Config ────────────────────────────────────────────
    private const string PingTarget  = "1.1.1.1";
    private const int    PingSamples = 4;
    private const int    TimeoutMs   = 2000;

    // ── Speed counters ────────────────────────────────────
    private PerformanceCounter? _dlCounter;
    private PerformanceCounter? _ulCounter;
    private bool _disposed;

    public NetworkMonitor()
    {
        try
        {
            // Pick the first active network adapter
            var cat       = new PerformanceCounterCategory("Network Interface");
            var instances = cat.GetInstanceNames();
            var adapter   = instances
                .Where(n => !n.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
                         && !n.Contains("Hyper-V",  StringComparison.OrdinalIgnoreCase)
                         && !n.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (adapter != null)
            {
                _dlCounter = new PerformanceCounter("Network Interface",
                    "Bytes Received/sec",    adapter);
                _ulCounter = new PerformanceCounter("Network Interface",
                    "Bytes Sent/sec",        adapter);

                // Prime
                _dlCounter.NextValue();
                _ulCounter.NextValue();
            }
        }
        catch { }
    }

    // ─────────────────────────────────────────────────────
    public NetworkSnapshot GetSnapshot()
    {
        var pings  = new List<float>(PingSamples);
        int failed = 0;

        for (int i = 0; i < PingSamples; i++)
        {
            try
            {
                using var ping  = new Ping();
                var reply       = ping.Send(PingTarget, TimeoutMs);
                if (reply.Status == IPStatus.Success)
                    pings.Add(reply.RoundtripTime);
                else
                    failed++;
            }
            catch { failed++; }
        }

        if (pings.Count == 0)
            return new NetworkSnapshot { Status = "Offline", IsOnline = false };

        float avg    = pings.Average();
        float jitter = pings.Count > 1
            ? pings.Zip(pings.Skip(1), (a, b) => MathF.Abs(b - a)).Average()
            : 0f;
        float loss   = (float)failed / PingSamples * 100f;

        // Speed (bytes/sec → Mb/sec)
        float dl = 0f, ul = 0f;
        try
        {
            if (_dlCounter != null) dl = _dlCounter.NextValue() * 8f / 1_000_000f;
            if (_ulCounter != null) ul = _ulCounter.NextValue() * 8f / 1_000_000f;
        }
        catch { }

        string status = avg switch
        {
            <= 0   => "Offline",
            < 40   => "Excellent",
            < 80   => "Good",
            < 150  => "Fair",
            _      => "High Latency",
        };

        return new NetworkSnapshot
        {
            PingMs        = MathF.Round(avg,    1),
            Jitter        = MathF.Round(jitter, 1),
            PacketLossPct = MathF.Round(loss,   1),
            DownloadMbps  = MathF.Round(dl,     2),
            UploadMbps    = MathF.Round(ul,     2),
            Status        = status,
            IsOnline      = true,
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dlCounter?.Dispose();
        _ulCounter?.Dispose();
    }
}
