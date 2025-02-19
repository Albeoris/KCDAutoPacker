namespace KCDAutoPacker;

public class AppOptions
{
    public required String WorkingDirectory { get; init; }
    public required String? ReleaseDirectory { get; init; }
    public required ConsoleLogger ConsoleLogger { get; init; }
    public required Boolean AnyFolder { get; init; }
}