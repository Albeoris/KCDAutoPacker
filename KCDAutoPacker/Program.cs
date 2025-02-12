using System.IO.Compression;

namespace KCDAutoPacker;

class Program
{
    private static Boolean _printErrorStack;
    private static String _workingDirectory;

    static Int32 Main(String[] args)
    {
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

        Console.WriteLine("Watching started. Press ENTER to stop and exit...");
        Console.ReadLine();
    }

    private static void InitialSync()
    {
        Console.WriteLine($"Initial sync...");
        String[] unpackedDirs = Directory.GetDirectories(_workingDirectory, "*.unpacked", SearchOption.AllDirectories);
        foreach (var dir in unpackedDirs)
        {
            String displayPath = GetDisplayPath(dir);
            try
            {
                SyncUnpackedFolder(dir);
            }
            catch (Exception ex)
            {
                PrintException($"Failed to sync {displayPath}", ex);
            }
        }
        Console.WriteLine($"Done.");
    }

    private static void OnChanged(String fullPath)
    {
        String unpackedFolder = FindUnpackedFolder(fullPath);
        if (unpackedFolder == null)
            return;
        
        if (File.Exists(fullPath))
        {
            try
            {
                File.OpenRead(fullPath).Close();
            }
            catch
            {
                // File is locked by another process
                return;
            }
        }
        
        String displayPath = GetDisplayPath(unpackedFolder);
        try
        {
            SyncUnpackedFolder(unpackedFolder);
        }
        catch (Exception ex)
        {
            PrintException($"Failed to sync {displayPath}", ex);
        }
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
        String folderName = Path.GetFileNameWithoutExtension(unpackedFolder);
        String archivePath = Path.Combine(parentDir, folderName + ".pak");

        var files = Directory.GetFiles(unpackedFolder, "*", SearchOption.AllDirectories);
        var diskFiles = new Dictionary<String, FileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            String relPath = Path.GetRelativePath(unpackedFolder, file);
            diskFiles[relPath] = new FileInfo(file);
        }

        String archiveDisplayPath = GetDisplayPath(archivePath);
        String folderDisplayPath = GetDisplayPath(unpackedFolder);

        if (!File.Exists(archivePath))
        {
            Console.WriteLine($"Packing [{folderDisplayPath}] into [{archiveDisplayPath}]");
            using (ZipArchive zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                foreach (var kv in diskFiles)
                {
                    zip.CreateEntryFromFile(kv.Value.FullName, kv.Key, CompressionLevel.Optimal);
                    Console.WriteLine($"\t Added: {kv.Key}");
                }
            }
            Console.WriteLine($"Done.");
        }
        else
        {
            Console.WriteLine($"Syncing [{folderDisplayPath}] with [{archiveDisplayPath}]");
            using (ZipArchive zip = ZipFile.Open(archivePath, ZipArchiveMode.Update))
            {
                var zipEntries = zip.Entries.ToDictionary(e => e.FullName, e => e, StringComparer.OrdinalIgnoreCase);

                foreach (var entry in zipEntries.Values.ToList())
                {
                    if (!diskFiles.ContainsKey(entry.FullName))
                    {
                        entry.Delete();
                        Console.WriteLine($"\t Removed: {entry.FullName} from {archiveDisplayPath}");
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
                            Console.WriteLine($"\t Updated: {entry.FullName} in {archiveDisplayPath}");
                        }
                    }
                    else
                    {
                        zip.CreateEntryFromFile(fi.FullName, relPath, CompressionLevel.Optimal);
                        Console.WriteLine($"\t Added: {kv.Key} in {archiveDisplayPath}");
                    }
                }
            }
        }
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