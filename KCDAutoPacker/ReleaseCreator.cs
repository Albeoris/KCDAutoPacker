using System.IO.Compression;

namespace KCDAutoPacker;

public class ReleaseCreator
{
    private readonly String _workingDirectory;
    private readonly String _releaseDirectory;
    private readonly ConsoleLogger _logger;

    public ReleaseCreator(String workingDirectory, String? releaseDirectory, ConsoleLogger logger)
    {
        _workingDirectory = workingDirectory;
        _releaseDirectory = ResolveReleaseDirectory(workingDirectory, releaseDirectory);
        _logger = logger;
    }

    public void PublishAll()
    {
        try
        {
            PublishAllInternal();
        }
        catch (Exception ex)
        {
            _logger.Exception("Failed to publish a new release.", ex);
        }
    }

    private void PublishAllInternal()
    {
        if (!_workingDirectory.EndsWith("Mods", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning("To create releases, the working directory must point to the Mods folder.");
            _logger.Warning($"Working directory: {_workingDirectory}");
            return;
        }

        if (GameMonitor.IsGameRunning())
        {
            _logger.Warning("Game is running. Mod files are locked and may be out of sync.");
            return;
        }

        // Create Release folder
        Directory.CreateDirectory(_releaseDirectory);

        // Process each mod folder in the working directory
        String[] modDirs = Directory.GetDirectories(_workingDirectory, "*", SearchOption.TopDirectoryOnly);
        String timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        Int32 found = 0;
        Int32 successed = 0;
        Int32 failed = 0;
        foreach (String modDir in modDirs)
        {
            String? zipPath = null;
            try
            {
                String modName = Path.GetFileName(modDir);
                String modReleaseFolderPath = Path.Combine(_releaseDirectory, modName);
                if (!Directory.EnumerateDirectories(modDir, "*.unpacked", SearchOption.AllDirectories).Any())
                    continue;
                
                found++;

                // Create mod folder in Release folder and generate zip archive
                Directory.CreateDirectory(modReleaseFolderPath);

                zipPath = Path.Combine(modReleaseFolderPath, $"{modName}-{timestamp}.zip");
                CreateModZipArchive(modDir, zipPath, modName);
                ConsoleLogger.ColorPrefix($"New release zip file created for mod: ", modName, ConsoleColor.Cyan);
                Console.WriteLine(zipPath);
                Console.WriteLine("--------------------------------");
                successed++;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.Exception($"Failed to publish the new release: {modDir}", ex);
                if (zipPath != null && File.Exists(zipPath))
                    File.Delete(zipPath);
            }
        }
        
        PrintStatistics(found, successed, failed);
        Console.WriteLine("--------------------------------");
    }

    private void CreateModZipArchive(String sourceDir, String zipPath, String modName)
    {
        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)
            .Where(f => !FileUtils.IsTempOrHiddenFile(f) &&
                        !FileUtils.IsOriginalFile(f) &&
                        !f.Contains(".unpacked", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (files.Length == 0)
        {
            Console.WriteLine($"Skipping empty mod folder [{modName}] to avoid creating an empty archive");
            return;
        }

        Dictionary<String, FileInfo> diskFiles = new(StringComparer.OrdinalIgnoreCase);
        foreach (String file in files)
        {
            String relPath = Path.GetRelativePath(sourceDir, file);
            diskFiles[relPath] = new FileInfo(file);
        }

        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var kv in diskFiles)
            {
                String entryPath = Path.Combine(modName, kv.Key);
                zip.CreateEntryFromFile(kv.Value.FullName, entryPath, CompressionLevel.Optimal);
                Console.WriteLine($"\tAdded: {kv.Key}");
            }
        }
    }

    private void PrintStatistics(Int32 found, Int32 successed, Int32 failed)
    {
        if (found == 0)
            _logger.Warning("Couldn't find any mods with .unpacked folders to create a new release.");

        if (successed == 0)
            Console.WriteLine("Failed to publish any mod releases.");
        else if (successed == 1)
            Console.WriteLine($"{successed} mod successfully published.");
        else
            Console.WriteLine($"{successed} mods successfully published.");
        
        if (failed == 1)
            Console.WriteLine($"Failed to publish {failed} mod.");
        else if (failed > 1)
            Console.WriteLine($"Failed to publish {failed} mods.");
    }

    private static String ResolveReleaseDirectory(String workingDirectory, String? releaseDirectory)
    {
        if (releaseDirectory is not null)
            return releaseDirectory;

        String parentDirectory = Path.GetDirectoryName(workingDirectory) ?? String.Empty;
        return Path.Combine(parentDirectory, "Mods-Dev", "Release");
    }
}