namespace JumpzysVortex.Services;

public class PerformanceSnapshot
{
    public DateTime Timestamp    { get; set; } = DateTime.Now;
    public float    Cpu          { get; set; }
    public float    Ram          { get; set; }
    public float    Gpu          { get; set; }
    public float    Fps          { get; set; }
    public float    CpuTemp      { get; set; }
    public float    GpuTemp      { get; set; }
    public long     AvailableRamMb { get; set; }
    public string?  GameName     { get; set; }
}
