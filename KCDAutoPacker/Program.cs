using System.CommandLine;

namespace KCDAutoPacker
{
    public static class Program
    {
        public static async Task<Int32> Main(String[] args)
        {
            var rootCommand = new RootCommand("KCDAutoPacker Application");

            Option<String> workingDirOption = new(
                "--working-directory",
                description: "Working directory",
                getDefaultValue: Directory.GetCurrentDirectory);

            Option<String?> releaseDirOption = new(
                "--release-directory",
                description: "Release directory",
                getDefaultValue: () => null);

            Option<Boolean> printErrorStackOption = new(
                "--print-error-stack",
                "Print full error stack on error");

            Option<Boolean> anyFolderOption = new(
                "--any-folder",
                "Allow any folder as working directory");

            rootCommand.AddOption(workingDirOption);
            rootCommand.AddOption(releaseDirOption);
            rootCommand.AddOption(printErrorStackOption);
            rootCommand.AddOption(anyFolderOption);

            rootCommand.SetHandler(MainWorkflow,
                workingDirOption, releaseDirOption, printErrorStackOption, anyFolderOption);

            return await rootCommand.InvokeAsync(args);
        }

        private static void MainWorkflow(String workingDirectory, String? releaseDirectory, Boolean printErrorStack, Boolean anyFolder)
        {
            ConsoleLogger consoleLogger = new(printErrorStack);
            AppOptions options = new() { WorkingDirectory = workingDirectory, ReleaseDirectory = releaseDirectory, ConsoleLogger = consoleLogger, AnyFolder = anyFolder };
            
            try
            {
                var app = new Application(options);
                app.Run();
            }
            catch (Exception ex)
            {
                consoleLogger.Exception($"Unexpected error.", ex);
            }
        }
    }
}
