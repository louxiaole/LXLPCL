using System.IO;
using System.Text.Json;

namespace LXLPCL.Core;

public static class JavaRuntimeInstaller
{
    private const string AllManifest = "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    public static string ComponentFor(int major, string? preferred = null)
    {
        if (!string.IsNullOrWhiteSpace(preferred)) return preferred!;
        if (major >= 21) return "java-runtime-delta";
        if (major >= 17) return "java-runtime-gamma";
        if (major >= 16) return "java-runtime-alpha";
        if (major >= 12) return "java-runtime-beta";
        return "jre-legacy";
    }

    public static async Task<JavaInfo> InstallAsync(int major, string? preferredComponent = null,
        CancellationToken token = default)
    {
        var component = ComponentFor(major, preferredComponent);
        DownloadCenter.Begin($"自动下载 Java {major}");
        try
        {
            DownloadCenter.Report("获取 Java 运行时信息...", 1, true);
            var allJson = await Http.GetStringAsync(Http.Candidates(AllManifest), token);
            using var allDoc = JsonDocument.Parse(allJson);
            if (!allDoc.RootElement.TryGetProperty("windows-x64", out var windows))
                throw new InvalidOperationException("没有适用于 Windows 64 位的 Java 运行时");
            if (!windows.TryGetProperty(component, out var componentEntry) ||
                componentEntry.ValueKind != JsonValueKind.Array || componentEntry.GetArrayLength() == 0)
                throw new InvalidOperationException($"没有找到 Java 运行时组件 {component}");

            var manifestUrl = componentEntry[0].GetProperty("manifest").GetProperty("url").GetString()
                              ?? throw new InvalidOperationException("Java 运行时清单缺少地址");
            DownloadCenter.Report("下载 Java 运行时清单...", 3, true);
            var manifestJson = await Http.GetStringAsync(Http.Candidates(manifestUrl), token);
            using var manifestDoc = JsonDocument.Parse(manifestJson);

            var targetDir = Path.Combine(AppState.RuntimeDir, component);
            var files = new List<(string path, string url, string? sha1)>();
            if (manifestDoc.RootElement.TryGetProperty("files", out var filesObj))
            {
                foreach (var prop in filesObj.EnumerateObject())
                {
                    if (!prop.Value.TryGetProperty("type", out var type) || type.GetString() != "file") continue;
                    if (!prop.Value.TryGetProperty("downloads", out var downloads)) continue;
                    if (!downloads.TryGetProperty("raw", out var raw)) continue;
                    var url = raw.TryGetProperty("url", out var u) ? u.GetString() : null;
                    if (string.IsNullOrEmpty(url)) continue;
                    var sha1 = raw.TryGetProperty("sha1", out var s) ? s.GetString() : null;
                    files.Add((Path.Combine(targetDir, prop.Name.Replace('/', Path.DirectorySeparatorChar)), url, sha1));
                }
            }
            if (files.Count == 0) throw new InvalidOperationException("Java 运行时清单为空");

            var done = 0;
            using var semaphore = new SemaphoreSlim(Math.Max(4, AppState.Config.DownloadThreads));
            var failures = 0;
            await Task.WhenAll(files.Select(async file =>
            {
                await semaphore.WaitAsync(token);
                try
                {
                    if (!Utils.HashMatches(file.path, file.sha1))
                    {
                        var urls = new List<string> { file.url };
                        urls.AddRange(Http.Candidates(file.url));
                        var ok = await Http.DownloadAsync(urls.Distinct().ToArray(), file.path, file.sha1, token);
                        if (!ok) Interlocked.Increment(ref failures);
                    }
                    var current = Interlocked.Increment(ref done);
                    if (current % 10 == 0 || current == files.Count)
                        DownloadCenter.Report($"Java 文件 {current}/{files.Count}", 5 + 94.0 * current / files.Count);
                }
                finally { semaphore.Release(); }
            }));

            if (failures > 0) throw new IOException($"有 {failures} 个 Java 文件下载失败");

            var info = JavaFinder.Inspect(targetDir)
                       ?? throw new InvalidOperationException("Java 运行时安装不完整");
            DownloadCenter.Complete("Java 已就绪");
            await AppState.RefreshJavasAsync();
            return info;
        }
        catch (OperationCanceledException)
        {
            DownloadCenter.Fail("已取消 Java 下载");
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error("自动下载 Java 失败: " + ex);
            DownloadCenter.Fail(ex.Message);
            throw;
        }
    }
}
