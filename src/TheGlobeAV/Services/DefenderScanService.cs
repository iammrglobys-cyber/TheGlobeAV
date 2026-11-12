using System.IO;
using System.Security.Principal;

namespace TheGlobeAV.Services;

public sealed partial class DefenderScanService
{
    public static string? FindMpCmdRun()
    {
        string platformRoot = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData),
            "Microsoft",
            "Windows Defender",
            "Platform");

        if (Directory.Exists(platformRoot))
        {
            try
            {
                var candidates = Directory
                    .EnumerateDirectories(platformRoot)
                    .Select(path =>
                    {
                        string name = Path.GetFileName(path);
                        string versionText = name.Split('-')[0];

                        Version.TryParse(
                            versionText,
                            out Version? version);

                        return new
                        {
                            Path = path,
                            Version = version ?? new Version(0, 0)
                        };
                    })
                    .OrderByDescending(item => item.Version);

                foreach (var candidate in candidates)
                {
                    string executable = Path.Combine(
                        candidate.Path,
                        "MpCmdRun.exe");

                    if (File.Exists(executable))
                        return executable;
                }
            }
            catch (IOException)
            {
                // Try the standard installation folder below.
            }
            catch (UnauthorizedAccessException)
            {
                // Try the standard installation folder below.
            }
        }

        string standard = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles),
            "Windows Defender",
            "MpCmdRun.exe");

        return File.Exists(standard) ? standard : null;
    }

    public static bool IsRunningElevated()
    {
        try
        {
            using WindowsIdentity identity =
                WindowsIdentity.GetCurrent();

            var principal = new WindowsPrincipal(identity);

            return principal.IsInRole(
                WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
