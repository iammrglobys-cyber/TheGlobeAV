namespace TheGlobeAV.Models;

public sealed class InspectionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Extension { get; set; } = "";
    public long FileLength { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime FinishedAtUtc { get; set; }
    public long DurationMilliseconds { get; set; }
    public string DetectedFormat { get; set; } = "";
    public string HeaderHex { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public bool IsArchive { get; set; }
    public int ArchiveEntryCount { get; set; }
    public bool ArchiveEntryListTruncated { get; set; }
    public List<string> ArchiveEntries { get; set; } = new();
    public List<InspectionEvent> Events { get; set; } = new();
    public bool WasCancelled { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StorageError { get; set; }
    public string? LocalRecordPath { get; set; }
}
