namespace TheGlobeAV.Models;

public sealed class FullScanSessionSummary
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime StartedAtUtc { get; set; }

    public DateTime FinishedAtUtc { get; set; }

    public long FilesDiscovered { get; set; }

    public long FoldersVisited { get; set; }

    public long FilesSkipped { get; set; }

    public long DiscoveryErrors { get; set; }

    public string InventoryCsvPath { get; set; } = "";

    public string ActivityLogPath { get; set; } = "";

    public string SummaryJsonPath { get; set; } = "";

    public List<DefenderScanResult> DefenderResults { get; set; }
        = new();

    public long DurationMilliseconds =>
        (long)(FinishedAtUtc - StartedAtUtc).TotalMilliseconds;
}
