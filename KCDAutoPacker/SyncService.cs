using System.Collections.Concurrent;
using System.IO.Compression;

namespace KCDAutoPacker;

public class SyncService
{
    private readonly String _workingDirectory;
    private readonly ConsoleLogger _consoleLogger;
    private readonly ConcurrentQueue<String> _pendingQueue = new();

    public SyncService(String workingDirectory, ConsoleLogger consoleLogger)
    {
        _workingDirectory = workingDirectory;
        _consoleLogger = consoleLogger;
    }

    // Enqueue all existing unpacked folders
    public void InitialSync()
    {
        String[] unpackedDirs = Directory.GetDirectories(_workingDirectory, "*.unpacked", SearchOption.AllDirectories);
        foreach (var dir in unpackedDirs)
            _pendingQueue.Enqueue(dir);
    }

    public void EnqueueFolder(String folder) => _pendingQueue.Enqueue(folder);

    public void ProcessQueue(CancellationToken token)
    {
        var failedFolders = new HashSet<String>();
        while (!token.IsCancellationRequested)
        {
            // Wait if game is running
            if (GameMonitor.IsGameRunning())
            {
                Console.WriteLine("Game is running. Waiting for it to exit...");
                while (GameMonitor.IsGameRunning())
                    token.WaitHandle.WaitOne(1000);
                Console.WriteLine("Game has exited. Proceeding with sync...");
            }

            if (_pendingQueue.IsEmpty)
            {
                token.WaitHandle.WaitOne(1000);
                continue;
            }

            var uniqueFolders = new HashSet<String>();
            while (_pendingQueue.TryDequeue(out var folder))
                uniqueFolders.Add(folder);

            if (uniqueFolders.Count == 0)
            {
                token.WaitHandle.WaitOne(1000);
                continue;
            }

            Boolean hasError = false;
            foreach (var folder in uniqueFolders)
            {
                try
                {
                    SyncUnpackedFolder(folder);
                    failedFolders.Remove(folder);
                }
                catch (Exception ex)
                {
                    hasError = true;
                    _pendingQueue.Enqueue(folder);
                    if (!failedFolders.Add(folder))
                        _consoleLogger.Exception($"Failed to sync {folder}.", ex);
                }
            }

            if (hasError)
            {
                token.WaitHandle.WaitOne(1000);
                continue;
            }

            LogCompletion();
        }
    }

    // Sync a single unpacked folder with its corresponding archive file
    private void SyncUnpackedFolder(String unpackedFolder)
    {
        String parentDir = Path.GetDirectoryName(unpackedFolder) ?? throw new InvalidOperationException();
        String pakName = Path.GetFileNameWithoutExtension(unpackedFolder); // name without ".unpacked"
        String archivePath = Path.Combine(parentDir, pakName + ".pak");
        String displayPath = FileUtils.GetDisplayPath(unpackedFolder, _workingDirectory);

        Console.WriteLine("--------------------------------");
        ConsoleLogger.ColorPrefix($"Syncing pack file: ", $"{pakName}.pak", ConsoleColor.Cyan);

        var files = Directory.GetFiles(unpackedFolder, "*", SearchOption.AllDirectories)
            .Where(f => !FileUtils.IsTempOrHiddenFile(f) && !FileUtils.IsOriginalFile(f))
            .ToArray();

        if (files.Length == 0)
        {
            Console.WriteLine($"Skipping empty mod folder [{displayPath}] to avoid accidental archive deletion.");
            return;
        }

        var diskFiles = new Dictionary<String, FileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            String relPath = Path.GetRelativePath(unpackedFolder, file);
            diskFiles[relPath] = new FileInfo(file);
        }

        if (!File.Exists(archivePath))
        {
            Console.WriteLine($"Making a new package file: {pakName}.pak");
            using (ZipArchive zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                foreach (var kv in diskFiles)
                {
                    zip.CreateEntryFromFile(kv.Value.FullName, kv.Key, CompressionLevel.Optimal);
                    Console.WriteLine($"\tAdded: {kv.Key}");
                }
            }
            ConsoleLogger.ColorPrefix($"The package has been successfully synchronized: ", $"{pakName}.pak", ConsoleColor.Cyan);
        }
        else
        {
            using (ZipArchive zip = ZipFile.Open(archivePath, ZipArchiveMode.Update))
            {
                var zipEntries = zip.Entries.ToDictionary(e => e.FullName, e => e, StringComparer.OrdinalIgnoreCase);
                Boolean changesFound = false;

                // Remove entries that no longer exist
                foreach (var entry in zipEntries.Values.ToList())
                {
                    if (!diskFiles.ContainsKey(entry.FullName))
                    {
                        entry.Delete();
                        Console.WriteLine($"\tRemoved: {entry.FullName}");
                        changesFound = true;
                    }
                }

                // Add new files or update changed files
                foreach (var kv in diskFiles)
                {
                    String relPath = kv.Key;
                    FileInfo fi = kv.Value;
                    if (zipEntries.TryGetValue(relPath, out var entry))
                    {
                        Boolean changed = entry.Length != fi.Length ||
                                          (entry.LastWriteTime.LocalDateTime - fi.LastWriteTime).Duration().TotalSeconds > 2;
                        if (changed)
                        {
                            entry.Delete();
                            zip.CreateEntryFromFile(fi.FullName, relPath, CompressionLevel.Optimal);
                            Console.WriteLine($"\tUpdated: {relPath}");
                            changesFound = true;
                        }
                    }
                    else
                    {
                        zip.CreateEntryFromFile(fi.FullName, relPath, CompressionLevel.Optimal);
                        Console.WriteLine($"\tAdded: {relPath}");
                        changesFound = true;
                    }
                }

                if (!changesFound)
                    ConsoleLogger.ColorPrefix("No changes found for package: ", $"{pakName}.pak", ConsoleColor.Gray);
                else
                    ConsoleLogger.ColorPrefix($"The package has been successfully synchronized: ", $"{pakName}.pak", ConsoleColor.Cyan);
            }
        }
    }

    private void LogCompletion()
    {
        Console.WriteLine("--------------------------------");
        Console.WriteLine("All unpacked mods have been processed. You can safely exit.");
    }
}