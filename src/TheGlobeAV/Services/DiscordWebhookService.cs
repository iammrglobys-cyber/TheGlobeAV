using System.IO.Compression;
using System.Net.Http.Headers;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheGlobeAV.Models;

namespace TheGlobeAV.Services;

public sealed class DiscordWebhookService : IDisposable
{
    private static readonly HttpClient Client = new();

    private static readonly string SecretPath = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "TheGlobeAV",
        "discord.secret");

    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true,
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob input,
        string? description,
        IntPtr entropy,
        IntPtr reserved,
        IntPtr prompt,
        uint flags,
        out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input,
        IntPtr description,
        IntPtr entropy,
        IntPtr reserved,
        IntPtr prompt,
        uint flags,
        out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static void ValidateWebhook(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals(
                "discord.com",
                StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(
                "/api/webhooks/",
                StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException(
                "Enter a valid Discord HTTPS webhook URL.");
        }
    }

    public void SaveWebhookUrl(string url)
    {
        ValidateWebhook(url);

        byte[] plain = Encoding.UTF8.GetBytes(url);

        DataBlob input = new()
        {
            cbData = plain.Length,
            pbData = Marshal.AllocHGlobal(plain.Length)
        };

        DataBlob output = default;

        try
        {
            Marshal.Copy(plain, 0, input.pbData, plain.Length);

            if (!CryptProtectData(
                ref input, "TheGlobeAV Discord webhook",
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                1, out output))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            byte[] encrypted = new byte[output.cbData];

            Marshal.Copy(
                output.pbData, encrypted, 0, output.cbData);

            string? directory = Path.GetDirectoryName(SecretPath);

            Directory.CreateDirectory(directory!);

            File.WriteAllBytes(SecretPath, encrypted);

            CryptographicOperations.ZeroMemory(encrypted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);

            if (input.pbData != IntPtr.Zero)
                Marshal.FreeHGlobal(input.pbData);

            if (output.pbData != IntPtr.Zero)
                LocalFree(output.pbData);
        }
    }

    public string? LoadWebhookUrl()
    {
        if (!OperatingSystem.IsWindows() ||
            !File.Exists(SecretPath))
            return null;

        byte[] encrypted = File.ReadAllBytes(SecretPath);

        DataBlob input = new()
        {
            cbData = encrypted.Length,
            pbData = Marshal.AllocHGlobal(encrypted.Length)
        };

        DataBlob output = default;

        try
        {
            Marshal.Copy(encrypted, 0, input.pbData,
                encrypted.Length);

            if (!CryptUnprotectData(
                ref input, IntPtr.Zero,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                1, out output))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());

            byte[] plain = new byte[output.cbData];

            Marshal.Copy(output.pbData, plain, 0,
                output.cbData);

            try
            {
                string url = Encoding.UTF8.GetString(plain);
                ValidateWebhook(url);
                return url;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);

            if (input.pbData != IntPtr.Zero)
                Marshal.FreeHGlobal(input.pbData);

            if (output.pbData != IntPtr.Zero)
                LocalFree(output.pbData);
        }
    }

    public async Task SendTestMessageAsync(
        CancellationToken cancellationToken = default)
    {
        string url = LoadWebhookUrl()
            ?? throw new InvalidOperationException(
                "Save a Discord webhook first.");

        string json = JsonSerializer.Serialize(new
        {
            content = "TheGlobeAV connection test successful. " +
                      "Discord reporting is connected."
        });

        using var content = new StringContent(
            json, Encoding.UTF8, "application/json");

        using var response = await Client.PostAsync(
            url, content, cancellationToken);

        response.EnsureSuccessStatusCode();
    }


    public async Task SendInspectionReportAsync(
        InspectionRecord record,
        CancellationToken cancellationToken = default)
    {
        string url = LoadWebhookUrl()
            ?? throw new InvalidOperationException(
                "Configure Discord reporting first.");

        string gif = new ReplayGifExporter()
            .CreateInspectionReplay(record);

        var files = new List<string> { gif };

        if (!string.IsNullOrWhiteSpace(record.LocalRecordPath) &&
            File.Exists(record.LocalRecordPath))
        {
            files.Add(record.LocalRecordPath);
        }

        var payload = new
        {
            embeds = new[]
            {
                new
                {
                    title = "TheGlobeAV - File Inspection",
                    color = 0x3688FF,
                    description =
                        "Recorded file inspection replay. " +
                        "This is not a malware verdict.",
                    fields = new object[]
                    {
                        new {
                            name = "File",
                            value = Truncate(record.FileName, 250),
                            inline = true
                        },
                        new {
                            name = "Format",
                            value = Truncate(record.DetectedFormat, 200),
                            inline = true
                        },
                        new {
                            name = "SHA-256",
                            value = Truncate(record.Sha256, 300),
                            inline = false
                        },
                        new {
                            name = "Archive entries",
                            value = record.ArchiveEntryCount.ToString(),
                            inline = true
                        },
                        new {
                            name = "Duration",
                            value = $"{record.DurationMilliseconds} ms",
                            inline = true
                        }
                    },
                    image = new {
                        url = $"attachment://{Path.GetFileName(gif)}"
                    },
                    footer = new {
                        text = "GlobeAV recorded inspection events"
                    }
                }
            }
        };

        await SendFileBatchesAsync(
            url, payload, files, cancellationToken);
    }

    public async Task SendFullScanReportAsync(
        FullScanSessionSummary summary,
        CancellationToken cancellationToken = default)
    {
        string url = LoadWebhookUrl()
            ?? throw new InvalidOperationException(
                "Configure Discord reporting first.");

        string folder =
            Path.GetDirectoryName(summary.SummaryJsonPath)
            ?? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "TheGlobeAV", "FullScanHistory");

        Directory.CreateDirectory(folder);

        string defenderPath = Path.Combine(
            folder, $"{summary.Id:N}-defender-report.txt");

        var report = new StringBuilder();

        report.AppendLine("THEGLOBEAV - MICROSOFT DEFENDER REPORT");
        report.AppendLine($"Started: {summary.StartedAtUtc:O}");
        report.AppendLine($"Finished: {summary.FinishedAtUtc:O}");
        report.AppendLine();

        foreach (var result in summary.DefenderResults)
        {
            report.AppendLine($"DRIVE: {result.FilePath}");
            report.AppendLine($"STATUS: {result.Status}");
            report.AppendLine($"EXIT CODE: {result.ExitCode}");
            report.AppendLine(
                $"DURATION: {result.DurationMilliseconds} ms");
            report.AppendLine(
                $"REMEDIATION DISABLED: {result.RemediationDisabled}");
            report.AppendLine($"ERROR: {result.ErrorMessage ?? "None"}");
            report.AppendLine("RAW OUTPUT:");
            report.AppendLine(result.Output);
            report.AppendLine("--------------------------------");
        }

        await File.WriteAllTextAsync(
            defenderPath, report.ToString(), cancellationToken);

        string gif = new ReplayGifExporter()
            .CreateFullScanReplay(summary.ActivityLogPath);


        string bundlePath = Path.Combine(
            folder, $"{summary.Id:N}-report-bundle.zip");

        using (var archive = ZipFile.Open(
            bundlePath, ZipArchiveMode.Create))
        {
            foreach (string path in new[]
            {
                summary.InventoryCsvPath,
                summary.ActivityLogPath,
                summary.SummaryJsonPath,
                defenderPath
            })
            {
                if (!string.IsNullOrWhiteSpace(path) &&
                    File.Exists(path))
                {
                    archive.CreateEntryFromFile(
                        path,
                        Path.GetFileName(path),
                        CompressionLevel.Optimal);
                }
            }
        }

        var files = new List<string>
        {
            gif,
            bundlePath
        };

        await SendFileBatchesAsync(
            url, payload, files, cancellationToken);
    }

    private static async Task SendFileBatchesAsync(
        string url,
        object firstPayload,
        List<string> filePaths,
        CancellationToken cancellationToken)
    {
        var available = filePaths
            .Where(File.Exists)
            .Distinct()
            .ToList();

        if (available.Count == 0)
            throw new FileNotFoundException(
                "No report attachments were found.");

        for (int offset = 0; offset < available.Count; offset += 10)
        {
            var batch = available.Skip(offset).Take(10).ToList();

            object payload = offset == 0
                ? firstPayload
                : new
                {
                    content = "Additional TheGlobeAV scan report attachments.",
                    allowed_mentions = new {
                        parse = Array.Empty<string>()
                    }
                };

            using var multipart = new MultipartFormDataContent();

            string payloadJson = JsonSerializer.Serialize(payload);

            multipart.Add(
                new StringContent(
                    payloadJson,
                    Encoding.UTF8,
                    "application/json"),
                "payload_json");

            for (int i = 0; i < batch.Count; i++)
            {
                string path = batch[i];

                var stream = File.OpenRead(path);

                var fileContent = new StreamContent(stream);

                fileContent.Headers.ContentType =
                    new MediaTypeHeaderValue("application/octet-stream");

                multipart.Add(
                    fileContent,
                    $"files[{i}]",
                    Path.GetFileName(path));
            }

            string requestUrl = url +
                (url.Contains('?') ? "&" : "?") +
                "wait=true";

            using var response = await Client.PostAsync(
                requestUrl, multipart, cancellationToken);

            string responseBody = await response.Content
                .ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Discord returned HTTP {(int)response.StatusCode}: " +
                    Truncate(responseBody, 1000));
            }
        }
    }

    private static string Truncate(string? value, int maximum)
    {
        if (string.IsNullOrEmpty(value))
            return "Not available";

        return value.Length <= maximum
            ? value
            : value[..maximum] + "...";
    }

    public void Dispose()
    {
        // HttpClient is shared by this service type.
    }
}
