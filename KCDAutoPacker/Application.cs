namespace KCDAutoPacker;

public class Application
{
    private readonly AppOptions _options;
    private readonly String _workingDirectory;
    private readonly SyncService _syncService;
    private readonly ReleaseCreator _releaseCreator;
    private readonly CancellationTokenSource _cts = new();

    public Application(AppOptions options)
    {
        _options = options;
        _workingDirectory = ResolveWorkingDirectory(options.WorkingDirectory, options.AnyFolder);
        _syncService = new SyncService(_workingDirectory, _options.ConsoleLogger);
        _releaseCreator = new ReleaseCreator(_workingDirectory, _options.ReleaseDirectory, _options.ConsoleLogger);
    }

    public void Run()
    {
        PrintWelcomeMessage();
        ConsoleLogger.ColorPrefix("Working directory: ", _workingDirectory, ConsoleColor.Green);

        // Initial sync: add all existing unpacked folders to queue
        _syncService.InitialSync();

        // Setup file watcher for changes
        using var watcher = new FileSystemWatcher(_workingDirectory);
        watcher.IncludeSubdirectories = true;
        watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size;
        watcher.Filter = "*.*";

        watcher.Changed += (_, e) => OnFileChanged(e.FullPath);
        watcher.Created += (_, e) => OnFileChanged(e.FullPath);
        watcher.Deleted += (_, e) => OnFileChanged(e.FullPath);
        watcher.Renamed += (_, e) => OnFileChanged(e.FullPath);
            
        watcher.EnableRaisingEvents = true;

        // Start background worker to process sync queue
        Task.Run(() => _syncService.ProcessQueue(_cts.Token), _cts.Token);

        Console.WriteLine("File watching started.");
        Console.WriteLine("Press 'R' to create a new release.");
        Console.WriteLine("Press ENTER to exit...");

        // Main loop: wait for key input
        while (true)
        {
            var key = Console.ReadKey(intercept: true).Key;
            if (key == ConsoleKey.Enter)
                break;
            
            if (key == ConsoleKey.R)
            {
                Console.WriteLine();
                Console.WriteLine("Confirm new release creation by pressing ENTER, or any other key to cancel...");
                if (Console.ReadKey(intercept: true).Key == ConsoleKey.Enter)
                {
                    _releaseCreator.PublishAll();
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("Release creation cancelled.");
                }
            }
        }

        _cts.Cancel();
    }

    private void OnFileChanged(String fullPath)
    {
        if (FileUtils.IsTempOrHiddenFile(fullPath))
            return;
        if (FileUtils.IsOriginalFile(fullPath))
            return;

        // Find the related unpacked folder and enqueue it for syncing
        var unpackedFolder = FileUtils.FindUnpackedFolder(fullPath, _workingDirectory);
        if (unpackedFolder != null)
            _syncService.EnqueueFolder(unpackedFolder);
    }

    private String ResolveWorkingDirectory(String directory, Boolean anyFolder)
    {
        directory = Path.GetFullPath(directory);
        if (!directory.Contains("Mods", StringComparison.OrdinalIgnoreCase) && !anyFolder)
            throw new Exception($"Working directory must be a subfolder of the 'Mods' directory. Use --any-folder to bypass. Directory: {directory}");
        return directory;
    }

    private void PrintWelcomeMessage()
    {
        String welcomeMessage = @"
╔════════════════════════════════════════════════════════╗
║  _  ___              _              ___                ║
║ | |/ (_)_ _  __ _ __| |___ _ __    / __|___ _ __  ___  ║
║ | ' <| | ' \/ _` / _` / _ \ '  \  | (__/ _ \ '  \/ -_) ║
║ |_|\_\_|_||_\__, \__,_\___/_|_|_|_ \___\___/_|_|_\___| ║
║        _       _         ___         _                 ║      
║       /_\ _  _| |_____  | _ \__ _ __| |_____ _ _       ║
║      / _ \ || |  _/ _ \ |  _/ _` / _| / / -_) '_|      ║
║     /_/_\_\_,_|\__\___/ |_| \__,_\__|_\_\___|_|        ║
║                                                        ║
╚════════════════════════════════════════════════════════╝
";
        Console.WriteLine(welcomeMessage);
    }
}