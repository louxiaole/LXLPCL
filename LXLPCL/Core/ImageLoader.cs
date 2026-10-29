using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace LXLPCL.Core;

/// <summary>带内存与磁盘缓存的图片加载器，用于 Modrinth 图标。</summary>
public static class ImageLoader
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly ConcurrentDictionary<string, BitmapImage> Memory = new();
    private static readonly ConcurrentDictionary<string, Task<BitmapImage?>> Pending = new();
    private static readonly string CacheDir = Path.Combine(AppState.CacheDir, "images");

    /// <summary>
    /// 读取版本的整合包图标：优先 versions/&lt;id&gt;/PCL/Logo.png，
    /// 其次 PCL/Setup.ini 中 Logo 指向的本地图片（pack:// 内置图标无法读取，返回 null）。
    /// </summary>
    public static BitmapImage? LoadVersionLogo(string versionFolder)
    {
        if (string.IsNullOrWhiteSpace(versionFolder)) return null;
        var pcl = Path.Combine(versionFolder, "PCL");
        var logo = LoadLocalFile(Path.Combine(pcl, "Logo.png"))
                   ?? LoadLocalFile(Path.Combine(pcl, "logo.png"));
        if (logo != null) return logo;

        try
        {
            var ini = Path.Combine(pcl, "Setup.ini");
            if (!File.Exists(ini)) return null;
            foreach (var line in File.ReadLines(ini, Encoding.UTF8))
            {
                var index = line.IndexOf(':');
                if (index <= 0) continue;
                if (!line[..index].Trim().Equals("Logo", StringComparison.OrdinalIgnoreCase)) continue;
                var value = line[(index + 1)..].Trim();
                if (value.StartsWith("pack://", StringComparison.OrdinalIgnoreCase)) return null;
                return LoadLocalFile(value);
            }
        }
        catch
        {
        }
        return null;
    }

    /// <summary>读取本地图片（用于整合包图标），失败时返回 null。</summary>
    public static BitmapImage? LoadLocalFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            if (!File.Exists(path)) return null;
            if (Memory.TryGetValue(path, out var cached)) return cached;
            var bitmap = Decode(File.ReadAllBytes(path));
            if (bitmap != null) Memory[path] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static Task<BitmapImage?> LoadAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return Task.FromResult<BitmapImage?>(null);
        if (Memory.TryGetValue(url, out var cached)) return Task.FromResult<BitmapImage?>(cached);
        return Pending.GetOrAdd(url, key => LoadCoreAsync(key));
    }

    private static async Task<BitmapImage?> LoadCoreAsync(string url)
    {
        try
        {
            var file = Path.Combine(CacheDir, Hash(url) + ".img");
            if (File.Exists(file))
            {
                try
                {
                    var fromDisk = Decode(File.ReadAllBytes(file));
                    if (fromDisk != null)
                    {
                        Memory[url] = fromDisk;
                        return fromDisk;
                    }
                }
                catch
                {
                    try { File.Delete(file); } catch { }
                }
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("LXLPCL/1.0");
            using var response = await Client.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength > 12 * 1024 * 1024) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes.Length == 0) return null;

            var bitmap = Decode(bytes);
            if (bitmap == null) return null;
            Memory[url] = bitmap;

            try
            {
                Directory.CreateDirectory(CacheDir);
                var temp = file + ".tmp";
                await File.WriteAllBytesAsync(temp, bytes).ConfigureAwait(false);
                File.Move(temp, file, true);
            }
            catch { }

            return bitmap;
        }
        catch
        {
            return null;
        }
        finally
        {
            Pending.TryRemove(url, out _);
        }
    }

    private static BitmapImage? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LXLPCL/1.0");
        return client;
    }
}
