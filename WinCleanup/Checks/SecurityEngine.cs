using WinCleanup.Utils;

namespace WinCleanup.Checks;

// Resolves which AV engine actually owns real-time protection.
// Defender exclusions are inert when a third-party engine is active, so
// Tier-0 must target the active engine (or defer to Tier-1 vendor guidance).
public static class SecurityEngine
{
    public sealed record State(string ActiveEngine, bool DefenderRealTime, List<string> ThirdParty);

    public static State Resolve(Logger log, string fixtureRoot, List<string> thirdPartyNames)
    {
        thirdPartyNames ??= [];
        // Fixture / non-Windows simulation: Defender assumed active so Tier-0
        // exclusion flow stays verifiable on Linux (Mitigate simulates anyway).
        if (!OperatingSystem.IsWindows())
        {
            log.Verbose($"engine: non-Windows simulation, thirdParty={thirdPartyNames.Count}");
            return new State(
                thirdPartyNames.Count > 0 ? thirdPartyNames[0] + " (simulated)" : "Windows Defender (simulated)",
                true, thirdPartyNames);
        }

        bool serviceRunning = SysProbe.Run("sc", "query windefend", 15000, log)
            .Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        // AMRunningMode: Normal = Defender active; Passive Mode = third-party owns it.
        string mode = SysProbe.PowerShell(
            "(Get-MpComputerStatus).AMRunningMode", 30000, log).Trim();
        bool defenderRealTime = serviceRunning &&
            (mode.Contains("Normal", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(mode));
        log.Verbose($"engine: windefend service={serviceRunning} AMRunningMode='{mode}' defenderRealTime={defenderRealTime}");

        string active = defenderRealTime ? "Windows Defender"
            : thirdPartyNames.Count > 0 ? thirdPartyNames[0] : "unknown";
        return new State(active, defenderRealTime, thirdPartyNames);
    }
}
