using System.Diagnostics;

namespace KCDAutoPacker;

public static class GameMonitor
{
    public static Boolean IsGameRunning()
    {
        return Process.GetProcessesByName("KingdomCome").Any(p => !p.HasExited);
    }
}