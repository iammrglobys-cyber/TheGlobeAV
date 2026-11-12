using System.Diagnostics;
using System.IO;
using TheGlobeAV.Models;

namespace TheGlobeAV.Services;

public sealed partial class DefenderScanService
{
    public async Task<List<DefenderScanResult>>
        ScanReadyDriveRootsAsync(
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
    {
        var results = new List<DefenderScanResult>();

        if (!OperatingSystem.IsWindows())
        {
            results.Add(new DefenderScanResult
            {
                FilePath = "Entire device",
                Status = DefenderScanStatus.Unavailable,
                ErrorMessage = "Windows is required."
            });

            return results;
        }

        if (!IsRunningElevated())
        {
            results.Add(new DefenderScanResult
            {
                FilePath = "Entire device",
                Status = DefenderScanStatus.Unavailable,
                ErrorMessage =
                    "Administrator permission is required."
            });

            return results;
        }

        string? executable = FindMpCmdRun();

        if (executable == null)
        {
            results.Add(new DefenderScanResult
            {
                FilePath = "Entire device",
                Status = DefenderScanStatus.Unavailable,
                ErrorMessage = "MpCmdRun.exe was not found."
            });

            return results;
        }

        var drives = DriveInfo.GetDrives()
            .Where(d =>
                d.IsReady &&
                (d.DriveType == DriveType.Fixed ||
                 d.DriveType == DriveType.Removable))
            .ToList();

        if (drives.Count == 0)
        {
            results.Add(new DefenderScanResult
            {
                FilePath = "Available drives",
                Status = DefenderScanStatus.Unavailable,
                ErrorMessage = "No ready local drives were found."
            });

            return results;
        }

        foreach (var drive in drives)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var timer = Stopwatch.StartNew();

            var result = new DefenderScanResult
            {
                FilePath = drive.RootDirectory.FullName,
                StartedAtUtc = DateTime.UtcNow,
                RemediationDisabled = true
            };

            progress?.Invoke(
                $"Microsoft Defender scanning {drive.Name}");

            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = executable,
                    WorkingDirectory =
                        Path.GetDirectoryName(executable)!,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // ScanType 3 = custom scan.
                // DisableRemediation is supported for custom scans.
                info.ArgumentList.Add("-Scan");
                info.ArgumentList.Add("-ScanType");
                info.ArgumentList.Add("3");
                info.ArgumentList.Add("-File");
                info.ArgumentList.Add(drive.RootDirectory.FullName);
                info.ArgumentList.Add("-DisableRemediation");

                using var process = new Process
                {
                    StartInfo = info
                };

                if (!process.Start())
                    throw new InvalidOperationException(
                        "Defender process could not start.");

                var stdout =
                    process.StandardOutput.ReadToEndAsync();

                var stderr =
                    process.StandardError.ReadToEndAsync();

                try
                {
                    await process.WaitForExitAsync(
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);

                    await process.WaitForExitAsync();

                    result.Status = DefenderScanStatus.Cancelled;

                    result.Output =
                        await stdout + "\n" + await stderr;

                    timer.Stop();

                    result.DurationMilliseconds =
                        timer.ElapsedMilliseconds;

                    result.FinishedAtUtc = DateTime.UtcNow;

                    results.Add(result);

                    throw;
                }

                result.ExitCode = process.ExitCode;

                result.Output =
                    await stdout + "\n" + await stderr;

                result.Status = process.ExitCode switch
                {
                    0 => DefenderScanStatus.Clean,
                    2 => DefenderScanStatus.Unconfirmed,
                    _ => DefenderScanStatus.Failed
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Status = DefenderScanStatus.Failed;
                result.ErrorMessage = ex.Message;
            }

            timer.Stop();

            result.DurationMilliseconds =
                timer.ElapsedMilliseconds;

            result.FinishedAtUtc = DateTime.UtcNow;

            results.Add(result);

            progress?.Invoke(
                $"Finished {drive.Name}: {result.Status}");
        }

        return results;
    }
}
