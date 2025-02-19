namespace KCDAutoPacker;

public sealed class ConsoleLogger
{
    private readonly Boolean _printErrorStack;

    public ConsoleLogger(Boolean printErrorStack)
    {
        _printErrorStack = printErrorStack;
    }
    
    public void Exception(String message, Exception ex)
    {
        if (_printErrorStack)
            Error($"{message} Error: {ex}");
        else
            Error($"{message} Error: {ex.Message}");
    }
    
    public void Error(String message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }
    
    public void Warning(String message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static void ColorPrefix<T>(String parameterName, T value, ConsoleColor foregroundColor)
    {
        Console.ForegroundColor = foregroundColor;
        Console.Write(parameterName);
        Console.ResetColor();
        Console.WriteLine(value);
    }
}