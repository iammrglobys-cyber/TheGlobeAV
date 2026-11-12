using System.IO;
using System.Text;
using TheGlobeAV.Models;

namespace TheGlobeAV.Services;

public sealed class ScanInventoryWriter
{
    private readonly object _lock = new();

    private StreamWriter? _csv;
    private StreamWriter? _activity;

    public string? CsvPath { get; private set; }
    public string? ActivityPath { get; private set; }

    private long _count;

    public long Count => Interlocked.Read(ref _count);

    public void Begin()
    {
        lock (_lock)
        {
            Complete();

            string folder = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "TheGlobeAV", "ScanHistory");

            Directory.CreateDirectory(folder);

            string id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

            CsvPath = Path.Combine(folder, $"inventory-{id}.csv");
            ActivityPath = Path.Combine(folder, $"activity-{id}.txt");

            _csv = new StreamWriter(
                CsvPath, false, new UTF8Encoding(true));

            _activity = new StreamWriter(
                ActivityPath, false, new UTF8Encoding(true));

            _csv.WriteLine(
                "File Name,Full Path,Extension,Size Bytes,Last Modified UTC");

            _activity.WriteLine("THE GLOBE AV - SCAN ACTIVITY");
            _activity.WriteLine($"Started UTC: {DateTime.UtcNow:O}");

            Interlocked.Exchange(ref _count, 0);
        }
    }

    public void RecordFile(ScannedFile file)
    {
        lock (_lock)
        {
            if (_csv == null)
                return;

            _csv.WriteLine(string.Join(",",
                Cell(file.Name),
                Cell(file.FullPath),
                Cell(file.Extension),
                Cell(file.SizeBytes.ToString()),
                Cell(file.LastModified.ToString("O"))));

            Interlocked.Increment(ref _count);

            if (Count % 100 == 0)
                _csv.Flush();
        }
    }

    public void RecordActivity(string message)
    {
        lock (_lock)
        {
            if (_activity == null)
                return;

            _activity.WriteLine(
                $"{DateTime.UtcNow:O} | {message}");

            if (message.StartsWith("Entering:",
                StringComparison.OrdinalIgnoreCase))
                _activity.Flush();
        }
    }

    private static string Cell(string value)
    {
        // CSV quoting and spreadsheet formula protection.
        if (value.Length > 0 &&
            "=+-@".Contains(value[0]))
            value = "'" + value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public void Complete()
    {
        lock (_lock)
        {
            _csv?.Flush();
            _csv?.Dispose();
            _csv = null;

            _activity?.Flush();
            _activity?.Dispose();
            _activity = null;
        }
    }
}
