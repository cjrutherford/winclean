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

Logging is verbose by default: every file decision (`DELETE`/`SKIP` reason with
age+size, `ENTER`/`EXIT` per directory, `PROBE` per OS tool) goes to both console
and timestamped log file. `--quiet` keeps the file log but shortens console.

## Manual verification without Windows (Linux)

```bash
dotnet build WinCleanup/WinCleanup.csproj -c Release
dotnet run --project WinCleanup/WinCleanup.csproj -c Release -- --self-test
# builds a fake Windows+QB tree in /tmp, dry-run (asserts nothing deleted),
# real clean (asserts junk gone + .qbw/.nd/.tlg/.qbb/.ecml/Data1.cab survive),
# inventory + bottleneck against fixture CSV. Expect result=PASS throughout.
dotnet run --project WinCleanup/WinCleanup.csproj -c Release -- \
  --audit-only --fixture-root=/tmp/<fixture> --verbose
```

## On Windows

```powershell
WinCleanup.exe --audit-only --verbose            # read-only audit
WinCleanup.exe --clean --dry-run --verbose       # safe preview of deletes
WinCleanup.exe --clean --yes                     # real run (Admin, QB closed)
```

Build the single-file exe (from Windows, or cross-publish):
```powershell
dotnet publish WinCleanup/WinCleanup.csproj -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:TargetFramework=net8.0-windows -p:EnableWindowsTargeting=true
```
