using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;

namespace LXLPCL.Core;

public class DownloadJob
{
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public double Percent { get; set; }
    public bool Active { get; set; }
    public bool Indeterminate { get; set; }
    public long SpeedBytes { get; set; }
    public string? Error { get; set; }
    public DateTime StartTime { get; set; } = DateTime.Now;

    public string SpeedText => Active && SpeedBytes > 0 ? Utils.FormatBytes(SpeedBytes) + "/s" : "";
}

public static class DownloadCenter
{
    private static readonly object Gate = new();
    private static DownloadJob _current = new();
    private static long _windowBytes;
    private static long _lastTicks = Stopwatch.GetTimestamp();
    private static DateTime _lastSpeedUpdate = DateTime.Now;

    public static DownloadJob Current
    {
        get { lock (Gate) return _current; }
    }

    public static event Action? Changed;

    public static void Begin(string title)
    {
        lock (Gate)
        {
            _current = new DownloadJob
            {
                Title = title,
                Active = true,
                Indeterminate = true,
                StartTime = DateTime.Now
            };
            Interlocked.Exchange(ref _windowBytes, 0);
            _lastTicks = Stopwatch.GetTimestamp();
        }
        Changed?.Invoke();
    }

    public static void Report(string detail, double percent, bool indeterminate = false)
    {
        lock (Gate)
        {
            _current.Detail = detail;
            _current.Percent = Math.Clamp(percent, 0, 100);
            _current.Indeterminate = indeterminate;
        }
        Changed?.Invoke();
    }

    public static void AddBytes(long bytes)
    {
        Interlocked.Add(ref _windowBytes, bytes);
        var now = DateTime.Now;
        if ((now - _lastSpeedUpdate).TotalMilliseconds < 400) return;
        lock (Gate)
        {
            var elapsed = (Stopwatch.GetTimestamp() - _lastTicks) / (double)Stopwatch.Frequency;
            if (elapsed > 0)
            {
                var total = Interlocked.Exchange(ref _windowBytes, 0);
                _current.SpeedBytes = (long)(total / elapsed);
                _lastTicks = Stopwatch.GetTimestamp();
                _lastSpeedUpdate = now;
            }
        }
    }

    public static void Complete(string? message = null)
    {
        lock (Gate)
        {
            _current.Active = false;
            _current.Indeterminate = false;
            _current.Percent = 100;
            _current.SpeedBytes = 0;
            if (message != null) _current.Detail = message;
        }
        Changed?.Invoke();
    }

    public static void Fail(string error)
    {
        lock (Gate)
        {
            _current.Active = false;
            _current.Indeterminate = false;
            _current.SpeedBytes = 0;
            _current.Error = error;
            _current.Detail = error;
        }
        Changed?.Invoke();
    }
}

public static class Http
{
    public static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxConnectionsPerServer = 16
        };
        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LXLPCL/1.0 (Minecraft Launcher)");
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        return client;
    }

    public static bool UseMirror => !AppState.Config.DownloadSource.Equals("官方", StringComparison.OrdinalIgnoreCase);

    public static string Mirror(string url)
    {
        if (!UseMirror) return url;
        const string libHost = "https://libraries.minecraft.net/";
        const string assetsHost = "https://resources.download.minecraft.net/";
        if (url.StartsWith(libHost, StringComparison.OrdinalIgnoreCase))
            return "https://bmclapi2.bangbang93.com/maven/" + url[libHost.Length..];
        if (url.StartsWith(assetsHost, StringComparison.OrdinalIgnoreCase))
            return "https://bmclapi2.bangbang93.com/assets/" + url[assetsHost.Length..];
        if (url.Contains("launchermeta.mojang.com", StringComparison.OrdinalIgnoreCase))
            return url.Replace("launchermeta.mojang.com", "bmclapi2.bangbang93.com", StringComparison.OrdinalIgnoreCase);
        if (url.Contains("piston-meta.mojang.com", StringComparison.OrdinalIgnoreCase))
            return url.Replace("piston-meta.mojang.com", "bmclapi2.bangbang93.com", StringComparison.OrdinalIgnoreCase);
        if (url.Contains("piston-data.mojang.com", StringComparison.OrdinalIgnoreCase))
            return url.Replace("piston-data.mojang.com", "bmclapi2.bangbang93.com", StringComparison.OrdinalIgnoreCase);
        const string modrinthHost = "https://cdn.modrinth.com/";
        if (url.StartsWith(modrinthHost, StringComparison.OrdinalIgnoreCase))
            return "https://mod.mcimirror.top/" + url[modrinthHost.Length..];
        return url;
    }

    public static string[] Candidates(string officialUrl, params string[] extra)
    {
        var list = new List<string>();
        if (UseMirror)
        {
            foreach (var item in extra)
                if (!string.IsNullOrWhiteSpace(item)) list.Add(item);
            var mirror = Mirror(officialUrl);
            if (mirror != officialUrl) list.Add(mirror);
        }
        list.Add(officialUrl);
        if (!UseMirror)
            foreach (var item in extra)
                if (!string.IsNullOrWhiteSpace(item)) list.Add(item);
        return list.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static async Task<byte[]> GetBytesAsync(string[] urls, CancellationToken token = default)
    {
        Exception? last = null;
        foreach (var url in urls)
        {
            try
            {
                using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync(token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                Logger.Error($"请求失败 {url}: {ex.Message}");
            }
        }
        throw last ?? new HttpRequestException("无法连接下载服务器");
    }

    public static async Task<string> GetStringAsync(string[] urls, CancellationToken token = default)
    {
        var bytes = await GetBytesAsync(urls, token);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    public static async Task<bool> DownloadAsync(string[] urls, string destination,
        string? sha1 = null, CancellationToken token = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ".");
        if (File.Exists(destination))
        {
            if (string.IsNullOrWhiteSpace(sha1) || Utils.HashMatches(destination, sha1)) return true;
            try { File.Delete(destination); } catch { }
        }

        var temp = destination + ".part";
        Exception? last = null;
        foreach (var url in urls)
        {
            try
            {
                using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? -1;
                long written = 0;
                await using (var input = await response.Content.ReadAsStreamAsync(token))
                await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await input.ReadAsync(buffer, token)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                        written += read;
                        DownloadCenter.AddBytes(read);
                    }
                }
                if (total > 0 && written != total)
                    throw new IOException($"文件大小不一致 ({written}/{total})");
                if (!string.IsNullOrWhiteSpace(sha1) && !Utils.HashMatches(temp, sha1))
                    throw new IOException("SHA1 校验失败");
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(temp, destination);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                Logger.Error($"下载失败 {url}: {ex.Message}");
            }
        }

        if (last != null) Logger.Error("文件下载失败: " + destination + " - " + last.Message);
        return false;
    }

    public static async Task<bool> DownloadFirstAsync(IEnumerable<string> urls, string destination,
        string? sha1 = null, CancellationToken token = default)
    {
        return await DownloadAsync(urls.ToArray(), destination, sha1, token);
    }
}
