using System.IO;

namespace TouchdownAlert.Overlay.Services;

/// <summary>
/// Plain-text rolling log written to %LOCALAPPDATA%/TouchdownAlert/overlay.log, since the overlay has
/// no console. Rolls (truncates by dropping the older half) once the file passes ~1 MB. All writes are
/// best-effort: logging must never crash the overlay.
/// </summary>
public sealed class FileLog
{
    private const long MaxBytes = 1 * 1024 * 1024;

    private readonly string _path;
    private readonly object _lock = new();

    public FileLog()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TouchdownAlert",
            "overlay.log"))
    {
    }

    public FileLog(string path)
    {
        _path = path;
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
        catch
        {
            // best effort
        }
    }

    public string Path_ => _path;

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                RollIfNeeded();
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(_path, line);
            }
        }
        catch
        {
            // Logging must never throw.
        }
    }

    private void RollIfNeeded()
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.Length < MaxBytes)
            {
                return;
            }

            var lines = File.ReadAllLines(_path);
            var keep = lines.Skip(lines.Length / 2).ToArray();
            File.WriteAllLines(_path, keep);
        }
        catch
        {
            // best effort
        }
    }
}
