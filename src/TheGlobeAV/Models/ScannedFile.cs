namespace TheGlobeAV.Models;

public enum FileScanStatus
{
    Discovered,
    Queued,
    Inspecting,
    Clean,
    Suspicious,
    ThreatDetected,
    Error,
    Skipped
}

public sealed class ScannedFile
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public string FullPath { get; set; } = "";

    public string Extension { get; set; } = "";

    public long SizeBytes { get; set; }

    public DateTime LastModified { get; set; }

    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;

    public FileScanStatus Status { get; set; }
        = FileScanStatus.Discovered;

    public string? Sha256 { get; set; }

    public string? DetectionName { get; set; }

    public string? ErrorMessage { get; set; }

    public string DisplaySize
    {
        get
        {
            double size = SizeBytes;

            string[] units = { "B", "KB", "MB", "GB", "TB" };

            int index = 0;

            while (size >= 1024 && index < units.Length - 1)
            {
                size /= 1024;
                index++;
            }

            return $"{size:0.##} {units[index]}";
        }
    }
}
