namespace WinCleanup.Checks;

// Shared contract for 0.2 offender detection (see docs/plans/2026-09-21-offender-mitigation-design.md).
// Score: 0-100. ConflictMultiplier: 2.0 dual-AV, 1.5 QB-blocker, else 1.0.
public sealed record Offender(
    string Category,      // SecurityConflicts|InboxAppx|UpdaterSprawl|OemBloat|QbBlockers
    string Name,          // e.g. "McAfee LiveSafe", "Microsoft.XboxApp", "AdobeARMservice task"
    int Score,
    double ConflictMultiplier,
    string Evidence,      // one-line why (verbose log carries details)
    string Tier,          // Tier0|Tier1|Never
    string FixHint        // exact command or manual step, "" when none
);
