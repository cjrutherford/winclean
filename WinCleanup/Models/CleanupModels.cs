namespace WinCleanup.Models;

public sealed class CleanupOptions
{
    public bool DoClean { get; set; }
    public bool DoAudit { get; set; } = true;
    public bool DryRun { get; set; } = true;
    public bool AssumeYes { get; set; }
    public int MinAgeDays { get; set; } = 7;
    public bool IncludePrefetch { get; set; }
    public bool AllowQbRunning { get; set; }
    public bool Verbose { get; set; } = true;
    // Manual verification without Windows: point cleaners at a fixture tree.
    public string FixtureRoot { get; set; } = "";
    // --self-test: build a fake Windows+QB tree, run dry-run + real clean, verify.
    public bool SelfTest { get; set; }
    // --offenders: run only the offender scan (+ firewall rules). --mitigate: apply Tier-0.
    public bool OffendersOnly { get; set; }
    public bool Mitigate { get; set; }
    // --lean: constrained-environment profile (startup impact + service/feature guidance).
    public bool Lean { get; set; }
    public string LogDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "WinCleanup", "logs");
}

public sealed class DeleteStats
{
    public long FilesDeleted;
    public long FilesSkippedLocked;
    public long FilesSkippedAge;
    public long FilesSkippedProtected;
    public long DirsRemoved;
    public long BytesFreed;
    public List<string> Errors { get; } = new();
}
