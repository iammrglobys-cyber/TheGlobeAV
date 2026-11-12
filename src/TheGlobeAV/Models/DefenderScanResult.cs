namespace TheGlobeAV.Models;

public enum DefenderScanStatus
{
    NotRun,
    Clean,
    AttentionRequired,
    Unconfirmed,
    Unavailable,
    Failed,
    Cancelled
}

public sealed class DefenderScanResult
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string FilePath { get; set; } = "";

    public string EngineName { get; set; }
        = "Microsoft Defender Antivirus";

    public DefenderScanStatus Status { get; set; }
        = DefenderScanStatus.NotRun;

    public DateTime StartedAtUtc { get; set; }

    public DateTime FinishedAtUtc { get; set; }

    public long DurationMilliseconds { get; set; }

    public int? ExitCode { get; set; }

    public string Output { get; set; } = "";

    public string? ErrorMessage { get; set; }

    public bool RemediationDisabled { get; set; }
}
