using WinCleanup.Utils;

namespace WinCleanup.Checks;

// Curated inbox-Appx table, aligned with Microsoft's 25H2
// "Remove Default Microsoft Store packages" policy list plus long-standing
// cruft (Cortana, People, Skype, Maps, Media3D). Safe = guided removal is low
// risk; Caution = check first (default handlers, Teams identity, dev tools).
// Package matching is by substring against Get-AppxPackage names.
public static class AppxCatalog
{
    public sealed record Entry(string Substring, string Note, bool Caution);

    public static readonly Entry[] All =
    [
        new("Xbox", "Gaming app + overlays (IDs: GamingApp, GamingOverlay, IdentityProvider, TCUI)", false),
        new("SolitaireCollection", "Microsoft Solitaire Collection", false),
        new("Clipchamp", "Video editor", false),
        new("BingNews", "Microsoft News", false),
        new("BingWeather", "MSN Weather", false),
        new("Copilot", "Microsoft 365 Copilot app (not the OS Copilot key)", false),
        new("Cortana", "Deprecated assistant; safe on 22H2+", false),
        new("FeedbackHub", "Feedback Hub", false),
        new("BingMaps", "Maps (24H2+ no longer inbox; harmless if present)", false),
        new("ZuneMusic", "Media Player legacy package name", false),
        new("ZuneVideo", "Movies & TV legacy package name", false),
        new("People", "People (deprecated)", false),
        new("SkypeApp", "Skype (deprecated)", false),
        new("MixedReality", "Mixed Reality Portal / 3D Viewer (no headset → remove)", false),
        new("3DViewer", "3D Viewer", false),
        new("StickyNotes", "Sticky Notes", false),
        new("ToDo", "Microsoft To Do", false),
        new("QuickAssist", "Quick Assist (keep on support PCs)", true),
        new("SoundRecorder", "Sound Recorder", false),
        // Caution group: default handlers, identity, dev tools.
        new("MicrosoftTeams", "Teams PERSONAL only — keep work/school Teams", true),
        new("Photos", "Default photo handler — confirm replacement first", true),
        new("MediaPlayer", "Default media handler — confirm replacement first", true),
        new("WindowsTerminal", "Dev machines: keep", true),
        new("Notepad", "Dev machines: keep", true),
        new("Paint", "Keep if used for quick edits", true),
        new("SnippingTool", "Keep — primary screenshot tool", true),
        new("Calculator", "Harmless; keep", true),
        new("Camera", "Keep on laptops/tablets", true),
        new("OutlookForWindows", "New Outlook — keep if it is the mail client", true),
    ];

    // Microsoft's policy-based debloat (24H2+/25H2 Enterprise/Education):
    // HKLM\SOFTWARE\Policies\Microsoft\Windows\Appx\RemoveDefaultMicrosoftStorePackages
    public static bool PolicyDebloatActive(Logger log)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Policies\Microsoft\Windows\Appx\RemoveDefaultMicrosoftStorePackages");
            bool active = k != null;
            log.Verbose($"appx: policy-based debloat key present={active}");
            return active;
        }
        catch (Exception ex) { log.Verbose($"appx: policy key check: {ex.Message}"); return false; }
    }
}
