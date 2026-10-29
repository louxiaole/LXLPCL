using System.IO;
using System.Text;

namespace LXLPCL.Core;

public static class Logger
{
    private static readonly object Gate = new();
    private static readonly List<string> Buffer = new();
    private static string _logFile = "";
    private static bool _initialized;

    public static event Action<string>? LineAdded;

    public static string LogFile => _logFile;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        var dir = Path.Combine(AppState.DataDir, "logs");
        Directory.CreateDirectory(dir);
        _logFile = Path.Combine(dir, "latest.log");
        try
        {
            if (File.Exists(_logFile) && new FileInfo(_logFile).Length > 4 * 1024 * 1024)
                File.Move(_logFile, Path.Combine(dir, $"lx-{DateTime.Now:yyyyMMdd-HHmmss}.log"), true);
        }
        catch { }
        Write("LXLPCL 已启动，版本 " + AppState.VersionText);
    }

    public static void Info(string message) => Write(message);

    public static void Error(string message) => Write("[错误] " + message);

    public static void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        lock (Gate)
        {
            Buffer.Add(line);
            if (Buffer.Count > 800) Buffer.RemoveRange(0, Buffer.Count - 800);
            try
            {
                if (!string.IsNullOrEmpty(_logFile))
                    File.AppendAllText(_logFile, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
        LineAdded?.Invoke(line);
    }

    public static string[] Tail(int count)
    {
        lock (Gate)
        {
            return Buffer.Skip(Math.Max(0, Buffer.Count - count)).ToArray();
        }
    }
}
