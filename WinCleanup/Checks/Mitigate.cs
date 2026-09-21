using Microsoft.Win32;
using WinCleanup.Cleaners;
using WinCleanup.Models;
using WinCleanup.Utils;

namespace WinCleanup.Checks;

// Tier-0 scriptable mitigations. Caller guarantees explicit consent before calling.
// BCL + SysProbe only (no new dependencies). Never throws: per-offender try/catch,
// failures are counted and reported in the log.
public static class Mitigate
{
    const string RunSubkey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    sealed record UndoRow(string Timestamp, string Offender, string Action, string Backup);
    sealed record RegBackup(string KeyPath, string ValueName, RegistryValueKind Kind, object? Data);

    public static int ApplyTier0(Logger log, CleanupOptions o, List<Offender> offenders, List<QuickBooksCleaner.Install> installs)
    {
        if (log is null) return 0;
        try
        {
            if (o is null) { log.Warn("Tier-0 skipped: null options"); return 0; }
            offenders ??= new List<Offender>();
            installs ??= new List<QuickBooksCleaner.Install>();

            bool isWindows = OperatingSystem.IsWindows();
            bool hasFixture = !string.IsNullOrEmpty(o.FixtureRoot);
            log.Verbose($"Mitigate start windows={isWindows} dryRun={o.DryRun} fixture={(hasFixture ? o.FixtureRoot : "<none>")} offenders={offenders.Count} installs={installs.Count}");

            if (!isWindows && !hasFixture)
            {
                log.Warn("Tier-0 needs Windows");
                return 0;
            }

            bool simulated = !isWindows; // FixtureRoot is non-empty here, so Linux self-test can simulate.
            bool dryRun = o.DryRun;

            int applied = 0;
            int skipped = 0;
            int failed = 0;
            var undoRows = new List<UndoRow>();
            var regBackups = new List<RegBackup>();

            foreach (var offender in offenders)
            {
                try
                {
                    if (offender is null) { log.Verbose("skip null offender entry"); skipped++; continue; }
                    string tier = offender.Tier ?? "";
                    string category = offender.Category ?? "";
                    string name = offender.Name ?? "";

                    if (!string.Equals(tier, "Tier0", StringComparison.OrdinalIgnoreCase))
                    {
                        log.Verbose($"skip non-Tier0: tier={tier} cat={category} name={name}");
                        skipped++;
                        continue;
                    }

                    // InboxAppx is Tier1 (guided-only); never touch here, even if mis-tiered.
                    if (string.Equals(category, "InboxAppx", StringComparison.OrdinalIgnoreCase))
                    {
                        log.Verbose($"skip InboxAppx (Tier1 guided-only, never Tier-0): name={name}");
                        skipped++;
                        continue;
                    }

                    // Startup Run-value removal: startup::<HKLM|HKCU>::<64|32>::<valuename>
                    if (name.StartsWith("startup::", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!TryParseStartup(name, out string hive, out string view, out string valueName))
                        {
                            log.Verbose($"no applier: malformed startup name '{name}' (want startup::<HKLM|HKCU>::<64|32>::<valuename>)");
                            skipped++;
                            continue;
                        }

                        if (dryRun)
                        {
                            log.Info($"[dry-run] would backup+delete Run value {hive}\\{RunSubkey} [{view}] :: {valueName} (offender={name})");
                            undoRows.Add(new UndoRow(Now(), name, "delete-run-value", "[dry-run] " + hive + " " + view + " " + valueName));
                            applied++;
                        }
                        else if (simulated)
                        {
                            log.Verbose($"simulate backup+delete Run value {hive}\\{RunSubkey} [{view}] :: {valueName} (offender={name})");
                            undoRows.Add(new UndoRow(Now(), name, "delete-run-value", "(simulated) " + hive + " " + view + " " + valueName));
                            regBackups.Add(new RegBackup(DisplayKeyPath(hive), valueName, RegistryValueKind.String, "(simulated)"));
                            applied++;
                        }
                        else if (TryDeleteRunValue(log, name, hive, view, valueName, undoRows, regBackups))
                        {
                            applied++;
                        }
                        else
                        {
                            skipped++;
                        }
                        continue;
                    }

                    // UpdaterSprawl: disable (never delete) the scheduled task.
                    if (string.Equals(category, "UpdaterSprawl", StringComparison.OrdinalIgnoreCase))
                    {
                        if (dryRun)
                        {
                            log.Info($"[dry-run] would run: schtasks /change /tn \"{name}\" /disable (offender={name})");
                            undoRows.Add(new UndoRow(Now(), name, "disable-scheduled-task", "[dry-run] " + name));
                            applied++;
                        }
                        else if (simulated)
                        {
                            log.Verbose($"simulate: schtasks /change /tn \"{name}\" /disable (offender={name})");
                            undoRows.Add(new UndoRow(Now(), name, "disable-scheduled-task", "(simulated) " + name));
                            applied++;
                        }
                        else
                        {
                            log.Verbose($"disable task: schtasks /change /tn \"{name}\" /disable (offender={name})");
                            SysProbe.Run("schtasks", $"/change /tn \"{name}\" /disable", 15000, log);
                            log.Info($"disabled scheduled task \"{name}\"");
                            undoRows.Add(new UndoRow(Now(), name, "disable-scheduled-task", name));
                            applied++;
                        }
                        continue;
                    }

                    // SecurityConflicts missing-exclusion: add Defender QB path exclusions only.
                    if (string.Equals(category, "SecurityConflicts", StringComparison.OrdinalIgnoreCase)
                        && name.Contains("exclusion", StringComparison.OrdinalIgnoreCase))
                    {
                        var dirs = installs
                            .Where(i => i is not null)
                            .SelectMany(i => new[] { i.ProgramDir, i.DataDir })
                            .Where(d => !string.IsNullOrWhiteSpace(d))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        log.Verbose($"exclusion offender '{name}': {dirs.Count} QB dirs from {installs.Count} installs");
                        if (dirs.Count == 0)
                        {
                            log.Verbose($"no applier: exclusion offender '{name}' but no QB ProgramDir/DataDir available; nothing to do");
                            skipped++;
                            continue;
                        }

                        if (dryRun)
                        {
                            foreach (var d in dirs)
                                log.Info($"[dry-run] would run: Add-MpPreference -ExclusionPath '{d}' (offender={name})");
                            foreach (var d in dirs)
                                undoRows.Add(new UndoRow(Now(), name, "add-mp-exclusion", "[dry-run] " + d));
                            applied++;
                        }
                        else if (simulated)
                        {
                            foreach (var d in dirs)
                                log.Verbose($"simulate: Add-MpPreference -ExclusionPath '{d}' (offender={name})");
                            foreach (var d in dirs)
                                undoRows.Add(new UndoRow(Now(), name, "add-mp-exclusion", "(simulated) " + d));
                            applied++;
                        }
                        else
                        {
                            foreach (var d in dirs)
                            {
                                string esc = d.Replace("'", "''");
                                log.Verbose($"add exclusion: Add-MpPreference -ExclusionPath '{esc}' (offender={name})");
                                SysProbe.PowerShell($"Add-MpPreference -ExclusionPath '{esc}'", 30000, log);
                                undoRows.Add(new UndoRow(Now(), name, "add-mp-exclusion", d));
                            }
                            log.Info($"added {dirs.Count} Defender exclusion(s) for offender {name}");
                            applied++;
                        }
                        continue;
                    }

                    // Anything else Tier0-typed (OemBloat, QbBlockers, other SecurityConflicts, ...).
                    log.Verbose($"no applier for Tier0 offender cat={category} name={name}; skipped (not counted)");
                    skipped++;
                }
                catch (Exception ex)
                {
                    failed++;
                    try { log.Warn($"Tier-0 failed offender={offender?.Name ?? "<null>"}: {ex.GetType().Name}: {ex.Message}"); } catch { }
                }
            }

            // Undo bundle: always written in live runs, never in dry-run.
            string ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string csvName = $"undo-{ts}.csv";
            string regName = $"undo-{ts}.reg";
            string logDir = o.LogDir ?? "";
            if (dryRun)
            {
                string wouldCsv = string.IsNullOrEmpty(logDir) ? csvName : Path.Combine(logDir, csvName);
                string extra = regBackups.Count > 0 ? " + " + regName + " (registry backup)" : "";
                log.Info($"[dry-run] would write undo bundle {wouldCsv} rows={undoRows.Count}{extra}; wrote nothing");
                log.Verbose($"[dry-run] undo bundle skipped: csv rows={undoRows.Count} reg values={regBackups.Count}");
            }
            else
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(logDir))
                    {
                        log.Verbose("LogDir empty; undo bundle skipped");
                    }
                    else
                    {
                        Directory.CreateDirectory(logDir);
                        string csvPath = Path.Combine(logDir, csvName);
                        File.WriteAllText(csvPath, BuildCsv(undoRows));
                        log.Verbose($"wrote undo csv {csvPath} rows={undoRows.Count}");
                        if (regBackups.Count > 0)
                        {
                            string regPath = Path.Combine(logDir, regName);
                            File.WriteAllText(regPath, BuildReg(regBackups));
                            log.Verbose($"wrote undo reg {regPath} values={regBackups.Count}");
                        }
                        else
                        {
                            log.Verbose("no registry values removed; .reg not written");
                        }
                    }
                }
                catch (Exception ex)
                {
                    try { log.Error($"undo bundle write failed: {ex.GetType().Name}: {ex.Message}"); } catch { }
                }
            }

            log.Info($"Tier-0 done applied={applied} skipped={skipped} failed={failed} dryRun={dryRun} simulated={simulated}");
            if (failed > 0)
            {
                try { log.Warn($"Tier-0 failures: {failed} (see warnings above)"); } catch { }
            }
            return applied;
        }
        catch (Exception ex)
        {
            try { log.Warn($"Tier-0 aborted: {ex.GetType().Name}: {ex.Message}"); } catch { }
            return 0;
        }
    }

    static bool TryParseStartup(string name, out string hive, out string view, out string valueName)
    {
        hive = "";
        view = "";
        valueName = "";
        var parts = name.Split("::", StringSplitOptions.None);
        if (parts.Length < 4) return false;
        if (!parts[0].Equals("startup", StringComparison.OrdinalIgnoreCase)) return false;
        hive = parts[1].Trim().ToUpperInvariant();
        view = parts[2].Trim();
        valueName = string.Join("::", parts.Skip(3));
        if ((hive != "HKLM" && hive != "HKCU") || (view != "64" && view != "32")) return false;
        if (string.IsNullOrEmpty(valueName)) return false;
        return true;
    }

    static string DisplayKeyPath(string hive) =>
        (hive == "HKLM" ? @"HKEY_LOCAL_MACHINE\" : @"HKEY_CURRENT_USER\") + RunSubkey;

    // Real-Windows Run-value removal: backup value data into the undo bundle, then delete.
    // Returns true when a value was actually removed.
    static bool TryDeleteRunValue(Logger log, string offenderName, string hive, string view,
        string valueName, List<UndoRow> undoRows, List<RegBackup> regBackups)
    {
        if (!OperatingSystem.IsWindows())
        {
            log.Verbose($"skip Run-value delete on non-Windows: {valueName}");
            return false;
        }
        RegistryHive h = hive == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
        RegistryView v = view == "64" ? RegistryView.Registry64 : RegistryView.Registry32;
        using var baseKey = RegistryKey.OpenBaseKey(h, v);
        using var key = baseKey.OpenSubKey(RunSubkey, writable: true);
        if (key is null)
        {
            log.Verbose($"Run key absent ({hive} [{view}]); nothing to remove for '{valueName}'");
            return false;
        }
        RegistryValueKind kind;
        object? data;
        try
        {
            kind = key.GetValueKind(valueName);
            data = key.GetValue(valueName);
        }
        catch (Exception ex)
        {
            log.Verbose($"Run value '{valueName}' absent ({hive} [{view}]): {ex.Message}; nothing to do");
            return false;
        }
        string backup = FormatBackup(kind, data);
        log.Verbose($"backup Run value '{valueName}' kind={kind} backup={backup} (offender={offenderName})");
        key.DeleteValue(valueName, throwOnMissingValue: false);
        log.Info($"removed Run value '{valueName}' ({hive} [{view}]); backup in undo bundle");
        undoRows.Add(new UndoRow(Now(), offenderName, "delete-run-value", $"{hive} {view} {valueName}={backup}"));
        regBackups.Add(new RegBackup(DisplayKeyPath(hive), valueName, kind, data));
        return true;
    }

    static string Now() => DateTime.Now.ToString("O");

    static string BuildCsv(List<UndoRow> rows)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("timestamp,offender,action,backup");
        foreach (var r in rows)
            sb.AppendLine($"{Csv(r.Timestamp)},{Csv(r.Offender)},{Csv(r.Action)},{Csv(r.Backup)}");
        return sb.ToString();
    }

    static string Csv(string s)
    {
        s ??= "";
        if (s.Contains('"') || s.Contains(',') || s.Contains('\r') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    // REGEDIT4 undo file restoring every removed Run value, grouped by key path.
    static string BuildReg(List<RegBackup> backups)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("REGEDIT4");
        sb.AppendLine();
        foreach (var g in backups.GroupBy(b => b.KeyPath, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"[{g.Key}]");
            foreach (var b in g)
                sb.AppendLine($"\"{b.ValueName.Replace("\"", "\"\"")}\"={FormatRegData(b.Kind, b.Data)}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    static string FormatRegData(RegistryValueKind kind, object? data) => kind switch
    {
        RegistryValueKind.String => $"\"{EscapeRegString(data?.ToString() ?? "")}\"",
        RegistryValueKind.ExpandString => "hex(2):" + HexUtf16((data?.ToString() ?? "") + "\0"),
        RegistryValueKind.MultiString => "hex(7):" + HexUtf16(MultiToFlat(data)),
        RegistryValueKind.DWord => $"dword:{Convert.ToUInt32(data ?? 0):x8}",
        RegistryValueKind.QWord => "hex(b):" + HexBytes(BitConverter.GetBytes(Convert.ToInt64(data ?? 0L))),
        RegistryValueKind.Binary when data is byte[] bytes => "hex:" + HexBytes(bytes),
        _ when data is byte[] bytes => "hex:" + HexBytes(bytes),
        _ => $"\"{EscapeRegString(data?.ToString() ?? "")}\"",
    };

    static string MultiToFlat(object? data)
    {
        if (data is string[] arr) return string.Join("\0", arr) + "\0\0";
        return (data?.ToString() ?? "") + "\0\0";
    }

    static string HexUtf16(string s) => HexBytes(System.Text.Encoding.Unicode.GetBytes(s));

    static string HexBytes(byte[] bytes) => string.Join(",", bytes.Select(b => b.ToString("x2")));

    static string EscapeRegString(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    static string FormatBackup(RegistryValueKind kind, object? data)
    {
        string s = (kind == RegistryValueKind.MultiString && data is string[] arr)
            ? string.Join(";", arr)
            : data is byte[] bytes ? BitConverter.ToString(bytes) : (data?.ToString() ?? "(null)");
        s = $"{kind}:{s}";
        return s.Length > 500 ? s[..500] + "…" : s;
    }
}
