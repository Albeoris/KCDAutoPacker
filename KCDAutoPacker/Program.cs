using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;

namespace KCDAutoPacker;

class Program
{
    private static Boolean _printErrorStack;
    private static Boolean _packOriginalFiles;
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
        _packOriginalFiles = args.Contains("--pack-original", StringComparer.InvariantCultureIgnoreCase);

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

        Console.WriteLine("Watching started. Press 'R' to create a new release. Press ENTER to stop and exit...");

        while (!cts.Token.IsCancellationRequested)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true).Key;
                if (key == ConsoleKey.R)  // If 'R' is pressed
                {
                    Console.WriteLine("\nAre you sure you want to create a new release? Press ENTER to confirm or any other key to cancel...");
                    if (Console.ReadKey(intercept: true).Key == ConsoleKey.Enter)
                    {
                        CreateReleaseFolderStructure();
                    }
                    else
                    {
                        Console.WriteLine("\nRelease creation cancelled.");
                    }
                }
            }
            Thread.Sleep(100);  // Give the system a little break to avoid CPU overload
        }

        Console.ReadLine();
        cts.Cancel();
    }

    private static void CreateReleaseFolderStructure()
    {
        // Path for the new Mods-Dev folder one step outside the current directory
        string parentDirectory = Directory.GetParent(_workingDirectory).FullName;
        string modsDevFolderPath = Path.Combine(parentDirectory, "Mods-Dev");

        // Create the Mods-Dev folder if it doesn't exist
        if (!Directory.Exists(modsDevFolderPath))
        {
            Directory.CreateDirectory(modsDevFolderPath);
            Console.WriteLine("--------------------------------");
            Console.WriteLine("Setting up 'Mods-Dev' folder at the game root directory....\n" +
                              "--------------------------------");
        }

        // Path for the Release folder inside Mods-Dev
        string releaseFolderPath = Path.Combine(modsDevFolderPath, "Release");

        // Create the Release folder if it doesn't exist
        if (!Directory.Exists(releaseFolderPath))
        {
            Directory.CreateDirectory(releaseFolderPath);
        }

        // Get the list of mod directories 
        string[] modDirs = Directory.GetDirectories(_workingDirectory, "*", SearchOption.TopDirectoryOnly);

        // Current timestamp for the zip filenames
        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        // Iterate through each mod folder
        foreach (var modDir in modDirs)
        {
            string? unpackedDir = null;
            string modDirName = Path.GetFileName(modDir);

            // Find any .unpacked directory in the mod's Data folder and only select the one that matches the mod's folder name
            string dataPath = Path.Combine(modDir, "Data");
            string[] unpackedDirs = Directory.Exists(dataPath)
                ? Directory.GetDirectories(dataPath, "*.unpacked")
                    .Where(dir => !string.IsNullOrEmpty(dir))
                    .ToArray()
                : Array.Empty<string>();

            if (unpackedDirs.Length == 0)
            {
                continue;  // Skip mods without any .unpacked directory
            }

            string unpackedDirName = unpackedDirs
                .Select(Path.GetFileNameWithoutExtension)
                .FirstOrDefault(name => string.Equals(
                    name,
                    modDirName,
                    StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

            if (string.IsNullOrEmpty(unpackedDirName))
            {
                Console.WriteLine("--------------------------------");
                Console.BackgroundColor = ConsoleColor.Red;
                Console.ForegroundColor = ConsoleColor.Black;
                Console.WriteLine($"Error:Mismatch between mod: {modDirName} root folder name and unpacked folder name: {Path.GetFileNameWithoutExtension(unpackedDirs[0])}\n");
                Console.WriteLine("Insure names are matching. IMPORTANT: after correction, make sure to manually delete the old .pak file");
                Console.ResetColor();
                Console.WriteLine("--------------------------------");
                continue;
            }

            if (string.Equals(modDirName, unpackedDirName, StringComparison.OrdinalIgnoreCase))
            {
                unpackedDir = Path.Combine(modDir, "Data", $"{modDirName}.unpacked");
            }

            if (unpackedDir != null && Directory.Exists(unpackedDir))  // Only process mods under development
            {
                // Create the mod folder inside the Release folder
                string modReleaseFolderPath = Path.Combine(releaseFolderPath, modDirName);
                if (!Directory.Exists(modReleaseFolderPath))
                {
                    Directory.CreateDirectory(modReleaseFolderPath);
                }

                // Create the release zip directly without temp files
                string zipPath = Path.Combine(modReleaseFolderPath, $"{modDirName}-{timestamp}.zip");
                CreateModZipArchive(modDir, zipPath, modDirName);
                Console.WriteLine($"new release zip file created for mod: {modDirName}\n--------------------------------");
            }
        }

        Console.WriteLine("New release completed.\n" +
                          "--------------------------------");
    }

    private static void CreateModZipArchive(string sourceDir, string zipPath, string modName)
    {
        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)
            .Where(f => !IsTempOrHiddenFile(f) && 
                       !IsOrgiginalFile(f) && 
                       !f.Contains(".unpacked"))
            .ToArray();

        if (files.Length == 0)
        {
            Console.WriteLine($"Skipping the empty mod folder [{modName}] to avoid creating an empty archive");
            return;
        }

        var diskFiles = new Dictionary<String, FileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            String relPath = Path.GetRelativePath(sourceDir, file);
            diskFiles[relPath] = new FileInfo(file);
        }

        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var kv in diskFiles)
            {
                zip.CreateEntryFromFile(kv.Value.FullName, kv.Key, CompressionLevel.Optimal);
                Console.WriteLine($"\t Added: {kv.Key}");
            }
        }
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
        if (IsOrgiginalFile(fullPath))
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
            .Where(f=> !IsTempOrHiddenFile(f) && !IsOrgiginalFile(f))
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
        if (fileInfo.Attributes.HasFlag(FileAttributes.Hidden))
            return true;
        return false;
    }
    private static Boolean IsOrgiginalFile(String fullPath)
    {
        return fullPath.Contains(".original");
    }

    private static void LogCompletion()
    {
        Console.WriteLine("--------------------------------\nAll unpacked mods have been processed. You can safely exit.");
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
        Console.BackgroundColor = ConsoleColor.Red;
        Console.ForegroundColor = ConsoleColor.Black;
        if (_printErrorStack)
            Console.WriteLine($"{message} Error: {ex}");
        else
            Console.WriteLine($"{message} Error: {ex.Message}");
        Console.ResetColor();
    }

    private static String GetDisplayPath(String dir)
    {
        return Path.GetRelativePath(_workingDirectory, dir);
    }
}