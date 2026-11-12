using System.IO;
using System.Text.Json;
using TheGlobeAV.Models;

namespace TheGlobeAV.Services;

public sealed class InspectionHistoryService
{
    private readonly string _historyFolder;

    public InspectionHistoryService()
    {
        _historyFolder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "TheGlobeAV",
            "InspectionHistory");
    }

    public async Task<List<InspectionRecord>> SearchAsync(
        string query = "",
        CancellationToken cancellationToken = default)
    {
        var results = new List<InspectionRecord>();

        if (!Directory.Exists(_historyFolder))
            return results;

        string[] files = Directory.GetFiles(
            _historyFolder,
            "*.json",
            SearchOption.TopDirectoryOnly);

        foreach (string file in files
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(1000))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                string json = await File.ReadAllTextAsync(
                    file, cancellationToken);

                var record =
                    JsonSerializer.Deserialize<InspectionRecord>(json);

                if (record == null)
                    continue;

                record.LocalRecordPath = file;

                bool matches =
                    string.IsNullOrWhiteSpace(query) ||
                    record.FileName.Contains(
                        query, StringComparison.OrdinalIgnoreCase) ||
                    record.FilePath.Contains(
                        query, StringComparison.OrdinalIgnoreCase);

                if (matches)
                    results.Add(record);
            }
            catch (JsonException)
            {
                // Ignore damaged or incompatible history records.
            }
            catch (IOException)
            {
                // Ignore temporarily inaccessible records.
            }
        }

        return results;
    }
}
