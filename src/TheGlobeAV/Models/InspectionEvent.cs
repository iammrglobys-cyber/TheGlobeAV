namespace TheGlobeAV.Models;

public sealed class InspectionEvent
{
    public long OffsetMilliseconds { get; set; }
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
