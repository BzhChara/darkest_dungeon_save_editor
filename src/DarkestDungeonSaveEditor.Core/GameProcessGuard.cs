using System.Diagnostics;

namespace DarkestDungeonSaveEditor.Core;

public static class GameProcessGuard
{
    public static bool IsRunning() => IsRunning(HasProcesses);

    internal static bool IsRunning(Func<string, bool> hasProcesses)
    {
        try { return hasProcesses("Darkest") || hasProcesses("DarkestDungeon"); }
        catch { return true; } // Only a successful inspection can authorize a write.
    }

    private static bool HasProcesses(string name)
    {
        var processes = Process.GetProcessesByName(name);
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    internal static void RequireStopped() => RequireStopped(IsRunning);

    internal static void RequireStopped(Func<bool> isRunning)
    {
        if (isRunning()) throw new InvalidOperationException(EditorText.Get("BattleMapEditService_030"));
    }
}
