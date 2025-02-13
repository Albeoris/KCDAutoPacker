using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using Microsoft.VisualBasic;

namespace KCDAutoPacker;

class Program
{
    private static Boolean _printErrorStack;
    private static String _workingDirectory;

    static Int32 Main(String[] args)
    {
        // Display the ASCII welcome message
        string welcomeMessage = @"
╔════════════════════════════════════════════════════════╗
║  _  ___              _              ___                ║
║ | |/ (_)_ _  __ _ __| |___ _ __    / __|___ _ __  ___  ║
║ | ' <| | ' \/ _` / _` / _ \ '  \  | (__/ _ \ '  \/ -_) ║
║ |_|\_\_|_||_\__, \__,_\___/_|_|_|_ \___\___/_|_|_\___| ║
║   /_\ _  _| |___/_  | _ \__ _ __| |_____ _ _           ║
║  / _ \ || |  _/ _ \ |  _/ _` / _| / / -_) '_|          ║
║ /_/_\_\_,_|\__\___/ |_| \__,_\__|_\_\___|_|            ║
║ | _ )_  _(_)   /_\ | | |__  ___ ___ _ _(_)___          ║
║ | _ \ || |_   / _ \| | '_ \/ -_) _ \ '_| (_-<          ║
║ |___/\_, (_) /_/ \_\_|_.__/\___\___/_| |_/__/          ║
║      |__/                                              ║
╚════════════════════════════════════════════════════════╝
";
        Console.WriteLine(welcomeMessage);
        _printErrorStack = args.Contains("--print-error-stack", StringComparer.InvariantCultureIgnoreCase);

        try
        {
            _workingDirectory = ResolveWorkingDirectory(args);
            Console.WriteLine($"Working directory: {_workingDirectory}");

            MainInternal();
            return 0;
        }
        catch (Exception ex)
        {
            PrintException("Unexpected error.", ex);

            Console.WriteLine("Press ENTER to exit...");
            Console.ReadLine();
            return 1;
        }
    }

    static void MainInternal()
    {
        InitialSync();

        using FileSystemWatcher watcher = new FileSystemWatcher(_workingDirectory);

        watcher.IncludeSubdirectories = true;
        watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size;
        watcher.Filter = "*.*";

        watcher.Changed += (s,e)=>OnChanged(e.FullPath);
        watcher.Created += (s,e)=>OnChanged(e.FullPath);
        watcher.Deleted += (s,e)=>OnChanged(e.FullPath);
        watcher.Renamed += (s,e)=>OnChanged(e.FullPath);
        watcher.EnableRaisingEvents = true;

        CancellationTokenSource cts = new CancellationTokenSource();
        Task.Run(()=>BackgroundWorker(cts.Token), cts.Token);

        Console.WriteLine("Watching started. Press ENTER to stop and exit...");
        Console.ReadLine();
        cts.Cancel();
    }

    private static void InitialSync()
    {
        String[] unpackedDirs = Directory.GetDirectories(_workingDirectory, "*.unpacked", SearchOption.AllDirectories);
        foreach (var dir in unpackedDirs)
            _pendingQueue.Enqueue(dir);
    }

    private static readonly ConcurrentQueue<String> _pendingQueue = new();

    private static void BackgroundWorker(CancellationToken token)
    {
        HashSet<String> failedFiles = new();
        while (!token.IsCancellationRequested)
        {
            Boolean theGameIsRunning = IsGameRunning();
            if (theGameIsRunning)
            {
                Console.WriteLine("The game is running and does not allow changing archives. Waiting for exit...");
                while (theGameIsRunning)
                {
                    token.WaitHandle.WaitOne(1000);
                    theGameIsRunning = IsGameRunning();
                }
                Console.WriteLine("The game has been finished. Syncing...");
            }

            if (_pendingQueue.Count == 0)
            {
                token.WaitHandle.WaitOne(1000);
                continue;
            }

            HashSet<String> uniqueFolder = new();
            while (_pendingQueue.TryDequeue(out String? folder))
                uniqueFolder.Add(folder);

            if (uniqueFolder.Count == 0)
            {
                token.WaitHandle.WaitOne(1000);
                continue;
            }

            Boolean hasError = false;
            foreach (String unpackedFolder in uniqueFolder)
            {
                try
                {
                    SyncUnpackedFolder(unpackedFolder);
                    failedFiles.Remove(unpackedFolder);
                }
                catch (Exception ex)
                {
                    hasError = true;
                    _pendingQueue.Enqueue(unpackedFolder);

                    if (!failedFiles.Add(unpackedFolder))
                        PrintException($"Failed to sync {unpackedFolder}.", ex);
                }
            }

            if (hasError)
            {
                token.WaitHandle.WaitOne(1000);
                continue;
            }

            // Log completion once all pending tasks are processed
            LogCompletion();
        }
    }

    private static Boolean IsGameRunning()
    {
        return Process.GetProcessesByName("KingdomCome").Any(p => !p.HasExited);
    }

    private static void OnChanged(String fullPath)
    {
        if (Path.GetExtension(fullPath) == ".tmp")
            return;

        if (IsTempOrHiddenFile(fullPath))
            return;

        String unpackedFolder = FindUnpackedFolder(fullPath);
        if (unpackedFolder == null)
            return;

        _pendingQueue.Enqueue(unpackedFolder);
    }

    private static String FindUnpackedFolder(String path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                if (path.EndsWith(".unpacked", StringComparison.OrdinalIgnoreCase))
                    return path;
            }
        }
        catch
        {
        }

        String dir = Path.GetDirectoryName(path);
        while (!String.IsNullOrEmpty(dir) && dir.Length >= _workingDirectory.Length)
        {
            if (dir.EndsWith(".unpacked", StringComparison.OrdinalIgnoreCase))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }


    private static void SyncUnpackedFolder(String unpackedFolder)
    {
        String parentDir = Path.GetDirectoryName(unpackedFolder);
        String folderName = Path.GetFileNameWithoutExtension(unpackedFolder);  // Get mod name (without ".unpacked")
        String archivePath = Path.Combine(parentDir, folderName + ".pak");
        String folderDisplayPath = GetDisplayPath(unpackedFolder);

        // Add a separator before logging each mod's details
        Console.WriteLine("--------------------------------");
        Console.WriteLine($"Syncing mod: [ {folderName} ]");

        var files = Directory.GetFiles(unpackedFolder, "*", SearchOption.AllDirectories)
            .Where(f=> !IsTempOrHiddenFile(f))
            .ToArray();
        
        if (files.Length == 0)
        {
            Console.WriteLine($"Skipping the empty mod folder [{folderDisplayPath}] so as not to accidentally delete the necessary archive");
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
            Console.WriteLine($"Packing mod: {folderName}");
            using (ZipArchive zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                foreach (var kv in diskFiles)
                {
                    zip.CreateEntryFromFile(kv.Value.FullName, kv.Key, CompressionLevel.Optimal);
                    Console.WriteLine($"\t Added: {kv.Key}");
                }
            }
            Console.WriteLine($"Done syncing mod: {folderName}");
        }
        else
        {
            using (ZipArchive zip = ZipFile.Open(archivePath, ZipArchiveMode.Update))
            {
                var zipEntries = zip.Entries.ToDictionary(e => e.FullName, e => e, StringComparer.OrdinalIgnoreCase);

                Boolean changesFound = false; // Track if any changes are found

                foreach (var entry in zipEntries.Values.ToList())
                {
                    if (!diskFiles.ContainsKey(entry.FullName))
                    {
                        entry.Delete();
                        Console.WriteLine($"\t Removed: {entry.FullName}");
                        changesFound = true;
                    }
                }

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
                            entry = zip.CreateEntryFromFile(fi.FullName, relPath, CompressionLevel.Optimal);
                            entry.LastWriteTime = fi.LastWriteTime;
                            Console.WriteLine($"\t Updated: {entry.FullName}");
                            changesFound = true;
                        }
                    }
                    else
                    {
                        zip.CreateEntryFromFile(fi.FullName, relPath, CompressionLevel.Optimal);
                        Console.WriteLine($"\t Added: {kv.Key}");
                        changesFound = true;
                    }
                }

                // If no changes were found, print a single clean message.
                if (!changesFound)
                {
                    Console.WriteLine($"No Changes found for mod: {folderName}");
                }
                else
                {
                    Console.WriteLine($"Done syncing mod: [ {folderName} ]");
                }
            }
        }
    }

    private static Boolean IsTempOrHiddenFile(String fullPath)
    {
        FileInfo fileInfo = new FileInfo(fullPath);
        if (fileInfo.Extension == ".tmp")
            return true;
        if (fileInfo.Attributes.HasFlag(FileAttribute.Hidden))
            return true;
        return false;
    }

    private static void LogCompletion()
    {
        Console.WriteLine("--------------------------------\n" +
            "All unpacked mods have been processed. You can safely exit.");
    }

    private static String ResolveWorkingDirectory(String[] args)
    {
        String directory = Directory.GetCurrentDirectory();
        if (args.Length > 0)
            directory = args[0];

        directory = Path.GetFullPath(directory);
        if (!directory.Contains("Mods") && !args.Contains("--any-folder", StringComparer.InvariantCultureIgnoreCase))
            throw new Exception($"Working directory must be subfolder for \"Mods\" directory. Please specify correct working directory or bypass this check by command line argument --any-folder. Working directory: {directory}");

        return directory;
    }

    private static void PrintException(String message, Exception ex)
    {
        if (_printErrorStack)
            Console.WriteLine($"{message} Error: {ex}");
        else
            Console.WriteLine($"{message} Error: {ex.Message}");
    }

    private static String GetDisplayPath(String dir)
    {
        return Path.GetRelativePath(_workingDirectory, dir);
    }
}