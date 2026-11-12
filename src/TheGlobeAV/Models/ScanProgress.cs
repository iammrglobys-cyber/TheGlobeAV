namespace TheGlobeAV.Models;

public sealed class ScanProgress
{
    public long FilesDiscovered { get; set; }

    public long FoldersVisited { get; set; }

    public long FilesSkipped { get; set; }

    public long Errors { get; set; }

    public string CurrentDirectory { get; set; } = "";

    public string CurrentFile { get; set; } = "";

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public bool IsComplete { get; set; }

    public TimeSpan Elapsed =>
        DateTime.UtcNow - StartedAt;
}
