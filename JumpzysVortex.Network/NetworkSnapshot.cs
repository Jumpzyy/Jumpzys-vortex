namespace JumpzysVortex.Network;

public class NetworkSnapshot
{
    public float  PingMs          { get; set; }
    public float  Jitter          { get; set; }
    public float  PacketLossPct   { get; set; }
    public float  DownloadMbps    { get; set; }
    public float  UploadMbps      { get; set; }
    public string Status          { get; set; } = "Unknown";
    public bool   IsOnline        { get; set; }
}
