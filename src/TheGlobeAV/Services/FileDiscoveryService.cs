using System.Collections.Concurrent;
using System.IO;
using TheGlobeAV.Models;

namespace TheGlobeAV.Services;

public sealed class FileDiscoveryService
{
    private readonly ConcurrentDictionary<string, byte> visited =
        new(StringComparer.OrdinalIgnoreCase);

    private long filesDiscovered;
    private long foldersVisited;
    private long filesSkipped;
    private long errors;

    public event Action<ScannedFile>? FileDiscovered;

    public event Action<ScanProgress>? ProgressChanged;

    public event Action<string>? Activity;

    public bool IsRunning { get; private set; }

    public bool IsCancelled { get; private set; }

    public long FilesDiscovered => Interlocked.Read(ref filesDiscovered);

    public long FoldersVisited => Interlocked.Read(ref foldersVisited);

    public long FilesSkipped => Interlocked.Read(ref filesSkipped);

    public long Errors => Interlocked.Read(ref errors);

    public async Task StartFullDiscoveryAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            throw new InvalidOperationException(
                "Discovery is already running.");

        Reset();

        IsRunning = true;

        try
        {
            Activity?.Invoke("Starting drive discovery.");

            var drives = DriveInfo.GetDrives();

            foreach (var drive in drives)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!drive.IsReady)
                {
                    Interlocked.Increment(ref filesSkipped);

                    Activity?.Invoke(
                        $"Skipped unavailable drive: {drive.Name}");

                    continue;
                }

                Activity?.Invoke(
                    $"Exploring drive: {drive.Name}");

                await DiscoverDirectoryAsync(
                    drive.RootDirectory.FullName,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            IsCancelled = true;

            Activity?.Invoke("Discovery cancelled.");
        }
        finally
        {
            IsRunning = false;

            Activity?.Invoke(
                $"Discovery finished. " +
                $"{FilesDiscovered:N0} files discovered.");

            PublishProgress("", true);
        }
    }

    public async Task StartFolderDiscoveryAsync(
        string folder,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            throw new InvalidOperationException(
                "Discovery is already running.");

        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("Folder path is empty.");

        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException(folder);

        Reset();

        IsRunning = true;

        try
        {
            Activity?.Invoke($"Exploring folder: {folder}");

            await DiscoverDirectoryAsync(
                folder,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            IsCancelled = true;

            Activity?.Invoke("Discovery cancelled.");
        }
        finally
        {
            IsRunning = false;

            Activity?.Invoke(
                $"Folder discovery finished. " +
                $"{FilesDiscovered:N0} files found.");

            PublishProgress("", true);
        }
    }

    private async Task DiscoverDirectoryAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!visited.TryAdd(directory, 0))
            return;

        try
        {
            Interlocked.Increment(ref foldersVisited);

            PublishProgress(directory, false);

            Activity?.Invoke($"Entering: {directory}");

            // Enumerate files first.

            IEnumerable<string> files;

            try
            {
                files = Directory.EnumerateFiles(
                    directory,
                    "*",
                    SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                Interlocked.Increment(ref filesSkipped);
                Activity?.Invoke($"Access denied: {directory}");
                return;
            }
            catch (IOException)
            {
                Interlocked.Increment(ref errors);
                return;
            }

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var info = new FileInfo(file);

                    var result = new ScannedFile
                    {
                        Name = info.Name,
                        FullPath = info.FullName,
                        Extension = info.Extension,
                        SizeBytes = info.Length,
                        LastModified = info.LastWriteTimeUtc,
                        Status = FileScanStatus.Discovered
                    };

                    Interlocked.Increment(ref filesDiscovered);

                    FileDiscovered?.Invoke(result);

                    PublishProgress(directory, false);
                }
                catch (UnauthorizedAccessException)
                {
                    Interlocked.Increment(ref filesSkipped);
                }
                catch (IOException)
                {
                    Interlocked.Increment(ref errors);
                }
            }

            // Enumerate child directories.

            IEnumerable<string> directories;

            try
            {
                directories = Directory.EnumerateDirectories(
                    directory,
                    "*",
                    SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                Interlocked.Increment(ref filesSkipped);
                return;
            }
            catch (IOException)
            {
                Interlocked.Increment(ref errors);
                return;
            }

            foreach (string child in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var attributes = File.GetAttributes(child);

                    // Never follow symbolic links or junctions.

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        Interlocked.Increment(ref filesSkipped);
                        continue;
                    }

                    await DiscoverDirectoryAsync(
                        child,
                        cancellationToken);
                }
                catch (UnauthorizedAccessException)
                {
                    Interlocked.Increment(ref filesSkipped);
                }
                catch (IOException)
                {
                    Interlocked.Increment(ref errors);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            Interlocked.Increment(ref filesSkipped);
        }
        catch (IOException)
        {
            Interlocked.Increment(ref errors);
        }
    }

    private void Reset()
    {
        visited.Clear();

        Interlocked.Exchange(ref filesDiscovered, 0);
        Interlocked.Exchange(ref foldersVisited, 0);
        Interlocked.Exchange(ref filesSkipped, 0);
        Interlocked.Exchange(ref errors, 0);

        IsCancelled = false;
    }

    private void PublishProgress(
        string directory,
        bool complete)
    {
        ProgressChanged?.Invoke(new ScanProgress
        {
            FilesDiscovered = FilesDiscovered,
            FoldersVisited = FoldersVisited,
            FilesSkipped = FilesSkipped,
            Errors = Errors,
            CurrentDirectory = directory,
            IsComplete = complete
        });
    }
}
