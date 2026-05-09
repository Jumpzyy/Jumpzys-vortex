namespace JumpzysVortex.Services;

public class BenchmarkSession
{
    public  string   Label       { get; }
    public  float    AvgFps      { get; private set; }
    public  float    OnePctLow   { get; private set; }
    public  float    AvgCpu      { get; private set; }
    public  float    AvgRam      { get; private set; }
    public  int      SampleCount { get; private set; }
    public  DateTime Timestamp   { get; } = DateTime.Now;

    private readonly List<PerformanceSnapshot> _snaps = [];

    public BenchmarkSession(string label) => Label = label;

    public void AddSnapshot(PerformanceSnapshot s)
    {
        _snaps.Add(s);
        SampleCount = _snaps.Count;

        var fps   = _snaps.Where(x => x.Fps > 0).Select(x => x.Fps).ToList();
        AvgFps    = fps.Any() ? fps.Average() : 0;
        AvgCpu    = _snaps.Average(x => x.Cpu);
        AvgRam    = _snaps.Average(x => x.Ram);

        if (fps.Count >= 5)
        {
            var sorted = fps.OrderBy(f => f).ToList();
            OnePctLow  = sorted.Take(Math.Max(1, sorted.Count / 100)).Average();
        }
    }

    public string CompareTo(BenchmarkSession baseline)
    {
        float fpsDelta = AvgFps    - baseline.AvgFps;
        float lowDelta = OnePctLow - baseline.OnePctLow;
        float cpuDelta = AvgCpu    - baseline.AvgCpu;

        string fpsArrow = fpsDelta > 0 ? "↑" : "↓";
        string lowArrow = lowDelta > 0 ? "↑" : "↓";
        string cpuArrow = cpuDelta < 0 ? "↓ (better)" : "↑";

        return $"BENCHMARK COMPARISON\n" +
               $"  [{baseline.Label}] vs [{Label}]\n" +
               $"  Avg FPS:  {baseline.AvgFps:F0} → {AvgFps:F0}  {fpsArrow} {MathF.Abs(fpsDelta):F1} fps\n" +
               $"  1% Low:   {baseline.OnePctLow:F0} → {OnePctLow:F0}  {lowArrow} {MathF.Abs(lowDelta):F1} fps\n" +
               $"  Avg CPU:  {baseline.AvgCpu:F0}% → {AvgCpu:F0}%  {cpuArrow}\n" +
               $"  Samples:  {baseline.SampleCount} baseline / {SampleCount} after\n" +
               $"  Captured: {Timestamp:HH:mm:ss}";
    }
}
