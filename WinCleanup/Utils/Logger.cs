namespace WinCleanup.Utils;

public sealed class Logger : IDisposable
{
    private readonly StreamWriter _w;
    public string Path { get; }
    public bool VerboseEnabled { get; set; } = true;

    public Logger(string dir, bool verbose = true)
    {
        VerboseEnabled = verbose;
        Directory.CreateDirectory(dir);
        Path = System.IO.Path.Combine(dir, $"wincleanup-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _w = new StreamWriter(Path, append: true) { AutoFlush = true };
    }

    public void Info(string m) => Write("INFO", m, console: true);
    public void Warn(string m) => Write("WARN", m, console: true);
    public void Error(string m) => Write("ERROR", m, console: true);
    // File: log-file only (used by Display mirrors so console isn't doubled).
    public void File(string m) => Write("INFO", m, console: false);
    // Verbose: always to file, to console only when VerboseEnabled.
    public void Verbose(string m) => Write("VERBOSE", m, console: VerboseEnabled);
    public void Debug(string m) => Write("DEBUG", m, console: VerboseEnabled);

    private void Write(string level, string m, bool console)
    {
        var line = $"[{DateTime.Now:O}] [{level}] {m}";
        if (console) Console.WriteLine(line);
        _w.WriteLine(line);
    }

    public void Dispose() => _w.Dispose();
}
