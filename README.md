# WinCleanup — Windows + QuickBooks-safe cleanup, audit & inventory

Single-file, zero-dependency .NET 8 console tool. No NuGet packages, no config files.

## What it does

1. **Windows temp/cache cleanup** (age-gated 7d default, skips locked files):
   `%TEMP%`, `C:\Windows\Temp`, per-user temps, `SoftwareDistribution\Download`
   (stops wuauserv/BITS first), Delivery Optimization cache, INetCache, CBS logs,
   IIS logs. Prefetch opt-in only. Recycle Bin emptied.
2. **QuickBooks-aware cleanup** (auto-detects all installed versions; safe-allowlist only):
   `QBUpdateCache`, `DownloadQB*`, `SPatch`/`EPatch`, `QBDataServiceUser*\Temp\search_data.*.dat`,
   `*.ADR.old`, `Qbwatch.log`/`qbupdate.log`.
   **Never touches**: `*.qbw .qbb .qbm .nd .tlg .ecml .qbp`, `PConfig\Data1.cab`,
   `EntitlementDataStore.ecml`, `qbprint.qbp`. Refuses to run while QBW32/QBDBMgr
   is open unless `--allow-qb-running`.
3. **Full audit**: disk/RAM/CPU, SMART, 7-day event-log errors, startup entries,
   Windows Update + pending reboot, network ping, QuickBooks config
   (services, company files + `.nd`/`.tlg` pairing, ADR state, license presence).
4. **Software inventory** (read-only): Add/Remove programs with sizes, startup entries,
   scheduled tasks, services. Flags QB/runtimes/drivers/AV as protected-from-touch.
5. **Bottleneck report**: prioritized, non-destructive actions. Tune, don't remove —
   only temp/cache/orphans are ever deleted.
6. **Worst-offender scan** (0.2): five detectors — dual real-time AV, inbox Appx bloat,
   updater-task sprawl, OEM bloat, QuickBooks blockers (AV exclusions, firewall ports,
   NAS company files, oversized `.tlg`). Ranked 0–100 with conflict multipliers,
   each with evidence and a fix hint. Included in every audit; `--offenders` runs it standalone.
7. **Startup impact + lean profile** (`--lean`): ranks autoruns by boot cost
   (high/medium, never AV/QB/drivers), and for constrained boxes recommends
   services to set Manual (tiny Tier-0 safe list: Xbox + Maps services, backed up),
   optional Windows features to review (guided DISM), and power/visual quick wins.
   QB hosts automatically exempt file-sharing services.

## Offender mitigation tiers (0.2)

| Tier | What | Examples |
|------|------|----------|
| Tier-0 (scriptable, consent only) | Applied by `--mitigate`; preview with `--dry-run` | Updater-task disable, Defender QB path exclusions, startup Run-value removal |
| Tier-1 (guided) | Tool prints exact commands; you run them | `Remove-AppxPackage` lines, AV keep-one flow, `netsh` QB port rules, OEM review list |
| Tier-2 (never-touch) | Tool refuses | AV/firewall uninstall or disable, QB data files, drivers, Windows features |

Tier-0 writes an undo bundle next to the log:
`undo-<timestamp>.csv` (every action + backup) and `undo-<timestamp>.reg`
(REGEDIT4, only when a startup value was removed — double-click to restore).
Mitigation is gated: type `MITIGATE` at the prompt, or pass `--yes`.
On a non-Windows box with `--fixture-root`, Tier-0 simulates (log only).

Logging is verbose by default: every file decision (`DELETE`/`SKIP` reason with
age+size, `ENTER`/`EXIT` per directory, `PROBE` per OS tool) goes to both console
and timestamped log file. `--quiet` keeps the file log but shortens console.
Every run prints a stage plan (`Plan: [1/5] …`) with `[k/n]` headers per phase,
sweep progress every 500 files, and a closing `SUMMARY` block (mode, top findings,
actions, next steps, log path) — followable without opening the log.

## Manual verification without Windows (Linux)

```bash
dotnet build WinCleanup/WinCleanup.csproj -c Release
dotnet run --project WinCleanup/WinCleanup.csproj -c Release -- --self-test
# builds a fake Windows+QB tree in /tmp, dry-run (asserts nothing deleted),
# real clean (asserts junk gone + .qbw/.nd/.tlg/.qbb/.ecml/Data1.cab survive),
# inventory + bottleneck against fixture CSV, offender scan (dual-AV, Appx,
# updater, OEM) + Tier-0 dry-run (asserts files unchanged). Expect PASS throughout.
dotnet run --project WinCleanup/WinCleanup.csproj -c Release -- \
  --audit-only --fixture-root=/tmp/<fixture> --verbose
dotnet run --project WinCleanup/WinCleanup.csproj -c Release -- \
  --offenders --mitigate --dry-run --fixture-root=/tmp/<fixture>
```

## On Windows

```powershell
WinCleanup.exe --audit-only --verbose            # read-only audit
WinCleanup.exe --clean --dry-run --verbose       # safe preview of deletes
WinCleanup.exe --clean --yes                     # real run (Admin, QB closed)
WinCleanup.exe --offenders --verbose             # ranked offender list + QB firewall rules
WinCleanup.exe --offenders --mitigate --dry-run  # preview Tier-0 fixes
WinCleanup.exe --offenders --mitigate --yes      # apply Tier-0 (Admin; writes undo bundle)
WinCleanup.exe --audit-only --lean               # + startup impact + constrained-box guidance
```

Build the single-file exe (from Windows, or cross-publish):
```powershell
dotnet publish WinCleanup/WinCleanup.csproj -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true
```
