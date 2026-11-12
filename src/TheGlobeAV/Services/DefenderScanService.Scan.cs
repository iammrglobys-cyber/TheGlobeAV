using System.IO;
using System.Diagnostics;
using TheGlobeAV.Models;

namespace TheGlobeAV.Services;

public sealed partial class DefenderScanService
{
    public async Task<DefenderScanResult> ScanFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();

        var result = new DefenderScanResult
        {
            FilePath = filePath,
            StartedAtUtc = DateTime.UtcNow,
            RemediationDisabled = true
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!OperatingSystem.IsWindows())
            {
                result.Status = DefenderScanStatus.Unavailable;
                result.ErrorMessage = "Windows is required.";
                return Finish(result, timer);
            }

            if (!File.Exists(filePath))
            {
                result.Status = DefenderScanStatus.Failed;
                result.ErrorMessage = "File does not exist.";
                return Finish(result, timer);
            }

            if (!IsRunningElevated())
            {
                result.Status = DefenderScanStatus.Unavailable;
                result.ErrorMessage =
                    "Administrator permission is required.";
                return Finish(result, timer);
            }

            string? executable = FindMpCmdRun();

            if (executable == null)
            {
                result.Status = DefenderScanStatus.Unavailable;
                result.ErrorMessage =
                    "Microsoft Defender command-line tool not found.";
                return Finish(result, timer);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory =
                    Path.GetDirectoryName(executable)!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-Scan");
            startInfo.ArgumentList.Add("-ScanType");
            startInfo.ArgumentList.Add("3");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(Path.GetFullPath(filePath));
            startInfo.ArgumentList.Add("-DisableRemediation");

            using var process = new Process
            {
                StartInfo = startInfo
            };

            if (!process.Start())
                throw new InvalidOperationException(
                    "Could not start Microsoft Defender.");

            Task<string> stdout =
                process.StandardOutput.ReadToEndAsync();

            Task<string> stderr =
                process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);

                await process.WaitForExitAsync();

                result.Output =
                    await stdout + "\n" + await stderr;

                result.Status = DefenderScanStatus.Cancelled;

                return Finish(result, timer);
            }

            result.ExitCode = process.ExitCode;

            result.Output =
                await stdout + "\n" + await stderr;

            // Microsoft documents 0 and 2 as ambiguous
            // in different circumstances. Code 2 can mean
            // malware, an action required, or a scan error.

            if (process.ExitCode == 0)
            {
                result.Status = DefenderScanStatus.Clean;
            }
            else if (process.ExitCode == 2)
            {
                result.Status = DefenderScanStatus.Unconfirmed;
            }
            else
            {
                result.Status = DefenderScanStatus.Failed;
            }
        }
        catch (OperationCanceledException)
        {
            result.Status = DefenderScanStatus.Cancelled;
        }
        catch (Exception ex)
        {
            result.Status = DefenderScanStatus.Failed;
            result.ErrorMessage = ex.Message;
        }

        return Finish(result, timer);
    }

    private static DefenderScanResult Finish(
        DefenderScanResult result,
        Stopwatch timer)
    {
        timer.Stop();

        result.DurationMilliseconds = timer.ElapsedMilliseconds;
        result.FinishedAtUtc = DateTime.UtcNow;

        return result;
    }
}
