using System.Diagnostics;
using System.Security.Principal;

namespace WinCleanup.Utils;

public static class AdminHelper
{
    public static bool IsAdmin()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool IsQuickBooksRunning()
    {
        if (!OperatingSystem.IsWindows()) return false;
        string[] names = ["QBW32", "QBDBMgr", "QBDBMgrN", "QBCFMonitorService", "QBUpdate"];
        var procs = Process.GetProcesses();
        return procs.Any(p =>
        {
            try { return names.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase); }
            catch { return false; }
        });
    }
}
