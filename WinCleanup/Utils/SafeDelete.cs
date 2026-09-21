using WinCleanup.Models;

namespace WinCleanup.Utils;

// Central safety gate: NEVER delete QuickBooks live data, age-gate, skip locked.
// Every decision is logged at VERBOSE level so runs are fully auditable.
public static class SafeDelete
{
    static readonly HashSet<string> NeverDeleteExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".qbw", ".qbb", ".qbm", ".nd", ".tlg", ".qba", ".qbx",
        ".ecml", ".qbp", ".cab", ".ini", ".dat" // .dat blocked only under QB program dirs; temp .dat handled explicitly
    };

    static readonly HashSet<string> NeverDeleteNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "EntitlementDataStore.ecml", "QBRegistration.dat", "qbprint.qbp",
        "wpr.ini", "Data1.cab"
    };

    public static bool IsProtected(string fullPath, string qbRootMarker)
    {
        var name = System.IO.Path.GetFileName(fullPath);
        if (NeverDeleteNames.Contains(name)) return true;
        // Strict protection only inside QuickBooks install / company-file dirs.
        if (qbRootMarker != "" && fullPath.StartsWith(qbRootMarker, StringComparison.OrdinalIgnoreCase))
        {
            var ext = System.IO.Path.GetExtension(fullPath);
            if (NeverDeleteExtensions.Contains(ext)) return true;
        }
        return false;
    }

    public static void DeleteFileAged(string file, TimeSpan minAge, bool dryRun, DeleteStats s, Logger log, string qbRoot = "")
    {
        FileInfo info;
        try
        {
            info = new FileInfo(file);
            if (!info.Exists) { log.Verbose($"MISS file-gone {file}"); return; }
        }
        catch (Exception ex) { s.Errors.Add($"{file}: stat failed: {ex.Message}"); log.Warn($"STAT-FAIL {file}: {ex.Message}"); return; }

        try
        {
            var age = DateTime.Now - info.LastWriteTime;
            if (IsProtected(file, qbRoot))
            {
                s.FilesSkippedProtected++;
                log.Verbose($"SKIP protected qbRoot='{qbRoot}' age={age.TotalDays:F1}d size={info.Length}B {file}");
                return;
            }
            if (age < minAge)
            {
                s.FilesSkippedAge++;
                log.Verbose($"SKIP too-new age={age.TotalDays:F1}d<{minAge.TotalDays:F0}d size={info.Length}B mtime={info.LastWriteTime:O} {file}");
                return;
            }
            s.BytesFreed += info.Length;
            if (dryRun)
            {
                s.FilesDeleted++;
                log.Verbose($"[dry-run] DELETE age={age.TotalDays:F1}d size={info.Length}B mtime={info.LastWriteTime:O} {file}");
                return;
            }
            log.Verbose($"DELETE age={age.TotalDays:F1}d size={info.Length}B {file}");
            info.Attributes &= ~FileAttributes.ReadOnly;
            info.Delete();
            s.FilesDeleted++;
        }
        catch (IOException ioex) { s.FilesSkippedLocked++; log.Verbose($"SKIP locked {file}: {ioex.Message}"); }
        catch (UnauthorizedAccessException uex) { s.FilesSkippedLocked++; s.Errors.Add($"{file}: {uex.Message}"); log.Verbose($"SKIP unauthorized {file}: {uex.Message}"); }
        catch (Exception ex) { s.Errors.Add($"{file}: {ex.Message}"); log.Warn($"SKIP error {file}: {ex.Message}"); }
    }

    public static void CleanDirAged(string dir, TimeSpan minAge, bool dryRun, DeleteStats s, Logger log, string qbRoot = "", string filePattern = "*")
    {
        log.Verbose($"ENTER dir={dir} pattern={filePattern} minAge={minAge.TotalDays}d dryRun={dryRun} qbRoot={qbRoot}");
        if (!Directory.Exists(dir)) { log.Verbose($"MISS dir-not-found {dir}"); return; }
        log.Info($"sweep {dir} (>{minAge.TotalDays}d)");
        string[] files;
        try { files = Directory.GetFiles(dir, filePattern, SearchOption.AllDirectories); }
        catch (Exception ex) { s.Errors.Add($"{dir}: {ex.Message}"); log.Warn($"LIST-FAIL {dir}: {ex.Message}"); return; }
        log.Verbose($"FOUND {files.Length} files under {dir}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int done = 0;
        foreach (var f in files)
        {
            DeleteFileAged(f, minAge, dryRun, s, log, qbRoot);
            if (++done % 500 == 0)
                log.Info($"  …{done}/{files.Length} files ({100 * done / files.Length}%) in {ShortDir(dir)}");
        }
        sw.Stop();
        log.Info($"swept {ShortDir(dir)}: {files.Length} files in {sw.Elapsed.TotalSeconds:F1}s");
        if (!dryRun)
        {
            int removed = 0;
            foreach (var d in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(d).Any())
                    { Directory.Delete(d); s.DirsRemoved++; removed++; log.Verbose($"RMDIR {d}"); }
                }
                catch (Exception ex) { log.Verbose($"SKIP rmdir {d}: {ex.Message}"); }
            }
            log.Verbose($"EXIT dir={dir} removedDirs={removed}");
        }
        else log.Verbose($"EXIT dir={dir} (dry-run, no rmdir)");
    }

    static string ShortDir(string dir)
    {
        try
        {
            string root = Path.GetPathRoot(dir) ?? "";
            string rest = dir[root.Length..].Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parts = rest.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return parts.Length <= 3 ? dir : Path.Combine(root, "…", Path.Combine(parts[^3], parts[^2], parts[^1]));
        }
        catch { return dir; }
    }
}
