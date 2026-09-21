# 0.2 Pilot validation — real Windows + QuickBooks machine

Goal: confirm no false positives before Tier-0 is ever used live.
Everything here is read-only except the final optional step. Run elevated
(Admin) with QuickBooks closed, company file backed up.

## 1. Audit + offender scan (read-only)

```powershell
.\WinCleanup.exe --audit-only --verbose > pilot-audit.log
.\WinCleanup.exe --offenders --verbose > pilot-offenders.log
```

Check in `pilot-offenders.log`:

- [ ] **Dual-AV**: flagged engines actually run real-time. One AV only → no finding.
      If Defender shows passive alongside a vendor AV, that is correct (no offender).
- [ ] **QB exclusion**: missing-exclusion is Tier-0 only when Defender owns
      real-time protection; with a vendor AV active it must be Tier-1 vendor guidance.
- [ ] **Appx**: every flagged package is actually unwanted. Caution rows (Teams,
      Photos, Terminal, Outlook…) must carry keep-notes. No false hits on
      work/school Teams.
- [ ] **Updater sprawl**: flagged tasks are genuine duplicates (Adobe ×2 etc.),
      not the single legitimate updater.
- [ ] **OEM**: drivers/firmware entries must NOT be flagged — only
      marketing/support assistants.
- [ ] **QB firewall**: printed ports match Database Server Manager > Port Monitor.
      2019+ must say dynamic (8019 + live port), never a guessed range.

## 2. Tier-0 dry-run (still read-only)

```powershell
.\WinCleanup.exe --offenders --mitigate --dry-run > pilot-mitigate-dry.log
```

- [ ] Every `[dry-run]` line names a real, intended target.
- [ ] No undo bundle files created (dry-run writes nothing).
- [ ] `SUMMARY` block lists the same actions as the dry-run lines.

## 3. Tier-0 live (optional, one category at a time)

Only after steps 1–2 are clean. Back up first (company file + system restore point).

```powershell
.\WinCleanup.exe --offenders --mitigate --yes > pilot-mitigate-live.log
```

- [ ] Undo bundle (`undo-<ts>.csv` [+ `.reg`]) exists in the log dir.
- [ ] Disabled tasks re-enable cleanly: `schtasks /change /tn "<name>" /enable`.
- [ ] `.reg` undo restores any removed startup value (double-click to apply).
- [ ] QuickBooks still opens the company file in its usual mode afterwards.

## 4. Report back

Paste any false positive as: offender line + evidence line + what the app/task
actually is. That feeds the AppxCatalog/vendor-table corrections before tagging v0.2.0.
