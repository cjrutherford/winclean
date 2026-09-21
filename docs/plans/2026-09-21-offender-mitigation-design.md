# WinCleanup 0.2 — Worst-offender detection + guided mitigation (plan)

Date: 2026-09-21 · Branch: `release-0.2` · Posture: **report + guided fixes**
Scope: **all five offender categories**. Principle carried over from 0.1:
tune, don't remove — only temp/cache/orphans are ever deleted automatically.

## 1. Offender taxonomy + scoring

Each offender gets a 0–100 impact score:
`score = cpuMemWeight + diskWeight + bootWeight, × conflictMultiplier`
(×2 for dual real-time AV, ×1.5 for anything blocking QuickBooks).

| # | Detector | Signals | Evidence logged |
|---|----------|---------|-----------------|
| 1 | SecurityConflicts | 2+ real-time AV engines (Defender state + Uninstall registry: Norton/McAfee/Trend/Avast/Kaspersky); missing QB path exclusions; firewall missing QB ports | engine list, exclusion list, port check per QB year |
| 2 | InboxAppx | Known-cruft Appx packages (Xbox, Solitaire, Clipchamp, News, Weather, Copilot, Cortana, Teams-personal, Feedback Hub, Maps…); provisioned vs installed | package full names + install dates |
| 3 | UpdaterSprawl | Ready/Running scheduled tasks matching updater patterns (Adobe, Google, Apple, OEM); multiple per vendor | task names, triggers, last-run |
| 4 | OemBloat | Publisher match (Dell/HP/Lenovo/Asus/Acer) in Add/Remove; OEM startup entries | app names, sizes, startup impact |
| 5 | QbBlockers | AV scanning QB dirs (no exclusion), firewall without 8019+dynamic ports, company file on NAS, `.tlg` > 500MB, search indexing on | port-monitor ports, file sizes, share path |

Output: ranked fix-list, highest score first, each with evidence lines
(verbose log) and a one-line summary.

## 2. Mitigation mechanics (tiers)

- **Tier-0 — scriptable, explicit consent only** (`--mitigate`, still dry-run first):
  - Startup Run-value removal **with `.reg` backup** of the value.
  - Defender QB path exclusions via `Add-MpPreference -ExclusionPath`
    (QB program + data dirs only; never broad exclusions).
  - Updater scheduled-task **disable** (not delete) for duplicates.
- **Tier-1 — guided (tool prints exact commands, user runs them)**:
  - Per-Appx `Remove-AppxPackage` / `Remove-AppxProvisionedPackage` lines.
  - AV keep-one decision flow (keep Defender or keep vendor — never auto-uninstall).
  - `netsh advfirewall` port rules per detected QB year (8019 + Port-Monitor ports).
  - OEM review list with sizes, keep-if-unsure.
- **Tier-2 — never-touch**: AV/firewall uninstall or disable, QB data files
  (`*.qbw/.nd/.tlg/.qbb`), drivers, Windows features, Store dependencies of kept apps.

Every action appends to the verbose log; Tier-0 writes a timestamped
undo bundle (`.reg` + `.csv`) under the log dir.

## 3. Implementation sketch

- `Checks/OffenderScan.cs` — five detectors, returns `List<Offender>` with scores.
- `Checks/Mitigate.cs` — Tier-0 appliers (consent gate, backup, log).
- `Checks/QbFirewall.cs` — port-monitor reader + `netsh` rule printer per QB year.
- `Utils/SysProbe.cs` — add `powershell -NoProfile -Command` runner (timeout, verbose).
- `Checks/SelfTest.cs` — fixture gains `installed-apps.csv` rows (second AV,
  OEM apps), fake Appx list `appx-packages.csv`, fake tasks `tasks.csv`;
  assert dual-AV flagged + Tier-0 dry-run changes nothing.
- `Program.cs` — `--offenders [--mitigate] [--yes]`; audit runs scan by default.
- Zero new NuGet deps (powershell.exe/schtasks/sc/netsh via SysProbe).

## 4. Verification

1. `dotnet build` clean, `dotnet run -- --self-test` PASS (incl. new offender asserts).
2. Manual: `--offenders --dry-run --verbose` on fixture; review ranked list.
3. Real-machine pilot: `--offenders` read-only first; Tier-0 only with `--mitigate --yes`.
4. Tag `v0.2.0`; Windows workflow attaches exe (already builds self-test on runner).

## Open questions for build phase

- Exact Appx allowlist/blocklist per Win11 24H2/25H2 (verify on pilot machine).
- QB year→port mapping table (2022/2023/2024 + dynamic Port-Monitor fallback).
- Defender exclusion UX when third-party AV owns real-time protection
  (exclusions must target the active engine — detect and route).
