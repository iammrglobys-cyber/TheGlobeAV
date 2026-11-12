using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TheGlobeAV.Models;

namespace TheGlobeAV.Services;

public sealed class FileInspectionService
{
    private const int MaxArchiveEntries = 500;
    private const int MaxArchiveEvents = 80;

    public async Task<InspectionRecord> InspectAsync(
        ScannedFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var timer = Stopwatch.StartNew();

        var record = new InspectionRecord
        {
            FileName = file.Name,
            FilePath = file.FullPath,
            Extension = file.Extension,
            StartedAtUtc = DateTime.UtcNow
        };

        void Log(string category, string message)
        {
            record.Events.Add(new InspectionEvent
            {
                OffsetMilliseconds = timer.ElapsedMilliseconds,
                Category = category,
                Message = message,
                TimestampUtc = DateTime.UtcNow
            });
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            Log("OPEN", "Opening file in read-only mode.");

            var info = new FileInfo(file.FullPath);

            if (!info.Exists)
                throw new FileNotFoundException(
                    "Selected file no longer exists.",
                    file.FullPath);

            record.FileLength = info.Length;

            Log("METADATA", $"Size: {info.Length:N0} bytes.");
            Log("METADATA", $"Extension: {info.Extension}");
            Log("METADATA", $"Modified: {info.LastWriteTimeUtc:O}");

            await using var stream = new FileStream(
                file.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            // Inspect the real file header.

            byte[] header = new byte[16];

            int count = await stream.ReadAsync(
                header.AsMemory(),
                cancellationToken);

            record.HeaderHex =
                Convert.ToHexString(header.AsSpan(0, count));

            record.DetectedFormat = IdentifyFormat(
                header.AsSpan(0, count),
                file.Extension);

            Log("HEADER", $"Read {count} header bytes.");
            Log("HEADER", record.DetectedFormat);

            // Calculate SHA-256 from the actual file bytes.

            cancellationToken.ThrowIfCancellationRequested();

            Log("HASH_START", "Calculating SHA-256.");

            stream.Position = 0;

            byte[] hash = await SHA256.HashDataAsync(
                stream,
                cancellationToken);

            record.Sha256 =
                Convert.ToHexString(hash).ToLowerInvariant();

            file.Sha256 = record.Sha256;

            Log("HASH_COMPLETE", "SHA-256 calculation finished.");
            Log("HASH_RESULT", record.Sha256);

            // JAR and ZIP archives use the ZIP format.

            bool zipExtension =
                file.Extension.Equals(
                    ".jar",
                    StringComparison.OrdinalIgnoreCase) ||
                file.Extension.Equals(
                    ".zip",
                    StringComparison.OrdinalIgnoreCase);

            bool zipHeader =
                count >= 4 &&
                header[0] == 0x50 &&
                header[1] == 0x4B;

            if (zipExtension || zipHeader)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Log("ARCHIVE", "Reading ZIP archive directory.");

                stream.Position = 0;

                using var archive = new ZipArchive(
                    stream,
                    ZipArchiveMode.Read,
                    leaveOpen: true);

                record.IsArchive = true;
                record.ArchiveEntryCount = archive.Entries.Count;

                Log("ARCHIVE",
                    $"Found {record.ArchiveEntryCount} entries.");

                int index = 0;

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (index < MaxArchiveEntries)
                        record.ArchiveEntries.Add(entry.FullName);

                    if (index < MaxArchiveEvents)
                        Log("ARCHIVE_ENTRY", entry.FullName);

                    index++;
                }

                record.ArchiveEntryListTruncated =
                    record.ArchiveEntryCount > MaxArchiveEntries;

                Log("ARCHIVE_COMPLETE",
                    "Archive directory inspection completed.");

                Log("ARCHIVE_NOTE",
                    "Entries were listed, not executed or extracted.");
            }
            else
            {
                Log("ARCHIVE_SKIP", "No ZIP/JAR archive identified.");
            }
        }
        catch (OperationCanceledException)
        {
            record.WasCancelled = true;
            Log("CANCELLED", "Inspection cancelled.");
        }
        catch (Exception ex)
        {
            record.ErrorMessage = ex.Message;
            Log("ERROR", $"{ex.GetType().Name}: {ex.Message}");
        }

        timer.Stop();

        record.DurationMilliseconds = timer.ElapsedMilliseconds;
        record.FinishedAtUtc = DateTime.UtcNow;

        Log("COMPLETE", "Inspection finished.");

        await SaveRecordAsync(record);

        return record;
    }

    private static string IdentifyFormat(
        ReadOnlySpan<byte> header,
        string extension)
    {
        if (header.Length >= 2 &&
            header[0] == 0x4D &&
            header[1] == 0x5A)
            return "Windows executable / PE candidate";

        if (header.Length >= 4 &&
            header[0] == 0x50 &&
            header[1] == 0x4B)
            return "ZIP-compatible archive";

        if (header.Length >= 4 &&
            header[0] == 0x7F &&
            header[1] == 0x45 &&
            header[2] == 0x4C &&
            header[3] == 0x46)
            return "ELF executable";

        if (header.Length >= 4 &&
            header[0] == 0x25 &&
            header[1] == 0x50 &&
            header[2] == 0x44 &&
            header[3] == 0x46)
            return "PDF document";

        return $"Unknown format (extension: {extension})";
    }

    private static async Task SaveRecordAsync(
        InspectionRecord record)
    {
        try
        {
            string root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrWhiteSpace(root))
                root = Path.GetTempPath();

            string folder = Path.Combine(
                root, "TheGlobeAV", "InspectionHistory");

            Directory.CreateDirectory(folder);

            string path = Path.Combine(
                folder, $"{record.Id:N}.json");

            string temp = path + ".tmp";

            string json = JsonSerializer.Serialize(
                record,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            await File.WriteAllTextAsync(temp, json);

            File.Move(temp, path, overwrite: true);

            record.LocalRecordPath = path;
        }
        catch (Exception ex)
        {
            record.StorageError = ex.Message;
        }
    }
}
