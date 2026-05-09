namespace JumpzysVortex.Services;

/// <summary>
/// Tracks FPS samples to compute 1% lows, frametime variance, and stutter events.
/// All properties are updated on every AddSample() call and are safe to read from any thread.
/// </summary>
public class FrametimeTracker
{
    private readonly Queue<float> _fpsSamples    = new();
    private readonly Queue<float> _frametimesMs  = new();
    private const    int          WindowSize      = 120; // ~2 sec at 60fps

    public float Current1PctLow     { get; private set; }
    public float CurrentAvgFps      { get; private set; }
    public float FrametimeStdDevMs  { get; private set; }
    public float FrametimeAvgMs     { get; private set; }
    public bool  StutterDetected    { get; private set; }
    public int   StutterEventCount  { get; private set; }

    public void AddSample(float fps)
    {
        if (fps <= 0) return;

        float ftMs = 1000f / fps;
        _fpsSamples.Enqueue(fps);
        _frametimesMs.Enqueue(ftMs);

        if (_fpsSamples.Count   > WindowSize) _fpsSamples.Dequeue();
        if (_frametimesMs.Count > WindowSize) _frametimesMs.Dequeue();

        if (_fpsSamples.Count < 10) return;
        Compute();
    }

    public void Reset()
    {
        _fpsSamples.Clear();
        _frametimesMs.Clear();
        StutterEventCount = 0;
        StutterDetected   = false;
    }

    private void Compute()
    {
        var sorted = _fpsSamples.OrderBy(f => f).ToList();
        int take   = Math.Max(1, sorted.Count / 100);

        Current1PctLow = sorted.Take(take).Average();
        CurrentAvgFps  = sorted.Average();

        var ftList    = _frametimesMs.ToList();
        FrametimeAvgMs = ftList.Average();
        FrametimeStdDevMs = MathF.Sqrt(
            ftList.Select(f => (f - FrametimeAvgMs) * (f - FrametimeAvgMs)).Average());

        // A stutter spike = frametime > 2× average (fps dropped to < half)
        float threshold  = FrametimeAvgMs * 2.0f;
        StutterEventCount = ftList.Count(f => f > threshold);
        StutterDetected   = StutterEventCount > 3;
    }
}
