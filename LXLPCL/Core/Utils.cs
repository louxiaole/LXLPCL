using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LXLPCL.Core;

public static class Utils
{
    public static string OfflineUuid(string name)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{hex[..8]}{hex[8..12]}{hex[12..16]}{hex[16..20]}{hex[20..]}";
    }

    public static string Sha1File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
    }

    public static bool HashMatches(string path, string? sha1)
    {
        if (string.IsNullOrWhiteSpace(sha1)) return File.Exists(path);
        try { return File.Exists(path) && Sha1File(path).Equals(sha1, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    public static string FormatBytes(double bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB" };
        var order = (int)Math.Floor(Math.Log(bytes, 1024));
        order = Math.Clamp(order, 0, units.Length - 1);
        return $"{bytes / Math.Pow(1024, order):0.#} {units[order]}";
    }

    public static string FormatDate(DateTime time) => time == default ? "从未" : time.ToString("yyyy-MM-dd HH:mm");

    /// <summary>PCL2 风格的时间描述，例如「3 天前」「2 个月前」。</summary>
    public static string RelativeTime(DateTime time)
    {
        if (time == default) return "";
        var span = DateTime.Now - time;
        if (span.TotalMinutes < 1) return "刚刚";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} 分钟前";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} 小时前";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays} 天前";
        if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)} 个月前";
        return $"{(int)(span.TotalDays / 365)} 年前";
    }

    public static string FormatNumber(long number) =>
        number switch
        {
            >= 100_000_000 => (number / 100_000_000.0).ToString("0.#") + "亿",
            >= 10_000 => (number / 10_000.0).ToString("0.#") + "万",
            _ => number.ToString("N0")
        };

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error("打开文件夹失败: " + ex.Message);
        }
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error("打开网页失败: " + ex.Message);
        }
    }

    public static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    /// <summary>去掉 Minecraft 格式化符号与不可见控制字符，合并多余空白。</summary>
    public static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var builder = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '§')
            {
                i++;
                continue;
            }
            builder.Append(char.IsControl(c) ? ' ' : c);
        }
        var joined = string.Join(" ", builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return joined.Trim('"', '\'', '`');
    }

    public static long DirectorySize(DirectoryInfo dir)
    {
        try { return dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch { return 0; }
    }
}
