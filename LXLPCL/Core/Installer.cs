using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace LXLPCL.Core;

public class RemoteVersion
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "release";
    public string Url { get; set; } = "";
    public DateTime ReleaseTime { get; set; }
    public bool IsLatestRelease { get; set; }
    public bool Installed { get; set; }

    public string TypeText => Type switch
    {
        "release" => "正式版",
        "snapshot" => "快照",
        "old_beta" => "远古 Beta",
        "old_alpha" => "远古 Alpha",
        _ => Type
    };

    public string DateText => ReleaseTime.ToString("yyyy-MM-dd");
    public string InstallText => Installed ? "已安装" : "安装";
}

public static class Installer
{
    public const string ManifestMirror = "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json";
    public const string ManifestOfficial = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private static List<RemoteVersion>? _cachedVersions;
    private static DateTime _cachedAt = DateTime.MinValue;

    public static async Task<List<RemoteVersion>> GetRemoteVersionsAsync(bool force = false, CancellationToken token = default)
    {
        var cacheFile = Path.Combine(AppState.CacheDir, "version_manifest.json");
        if (!force && _cachedVersions != null && (DateTime.Now - _cachedAt).TotalMinutes < 30)
            return _cachedVersions;
        if (!force && _cachedVersions == null && File.Exists(cacheFile) &&
            (DateTime.Now - File.GetLastWriteTime(cacheFile)).TotalMinutes < 30)
        {
            try
            {
                var cachedJson = await File.ReadAllTextAsync(cacheFile, token);
                var cached = ParseManifest(cachedJson);
                if (cached.Count > 0)
                {
                    _cachedVersions = cached;
                    _cachedAt = DateTime.Now;
                    return cached;
                }
            }
            catch { }
        }

        try
        {
            var json = await Http.GetStringAsync(Http.Candidates(ManifestOfficial, ManifestMirror), token);
            var list = ParseManifest(json);
            if (list.Count > 0)
            {
                _cachedVersions = list;
                _cachedAt = DateTime.Now;
                await File.WriteAllTextAsync(cacheFile, json, token);
                return list;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Error("获取版本列表失败: " + ex.Message);
        }

        if (File.Exists(cacheFile))
        {
            try
            {
                var cached = ParseManifest(await File.ReadAllTextAsync(cacheFile, token));
                if (cached.Count > 0)
                {
                    _cachedVersions = cached;
                    return cached;
                }
            }
            catch { }
        }
        throw new InvalidOperationException("无法获取 Minecraft 版本列表，请检查网络连接");
    }

    private static List<RemoteVersion> ParseManifest(string json)
    {
        var result = new List<RemoteVersion>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var latestRelease = "";
        if (root.TryGetProperty("latest", out var latest) && latest.TryGetProperty("release", out var release))
            latestRelease = release.GetString() ?? "";

        if (!root.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var item in versions.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(id)) continue;
            var time = DateTime.MinValue;
            if (item.TryGetProperty("releaseTime", out var timeProp) &&
                DateTime.TryParse(timeProp.GetString(), out var parsed)) time = parsed.ToLocalTime();
            result.Add(new RemoteVersion
            {
                Id = id,
                Type = item.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "release" : "release",
                Url = item.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? "" : "",
                ReleaseTime = time,
                IsLatestRelease = id == latestRelease
            });
        }
        return result;
    }

    public static async Task InstallVanillaAsync(string id, bool installAssets = true,
        CancellationToken token = default)
    {
        DownloadCenter.Begin("安装 Minecraft " + id);
        try
        {
            var versions = await GetRemoteVersionsAsync(false, token);
            var remote = versions.FirstOrDefault(v => v.Id == id)
                         ?? throw new InvalidOperationException("未找到版本 " + id);

            DownloadCenter.Report("获取版本信息...", 1, true);
            var versionDir = Path.Combine(AppState.Config.GameDir, "versions", id);
            Directory.CreateDirectory(versionDir);
            var jsonPath = Path.Combine(versionDir, id + ".json");
            var versionJson = await Http.GetBytesAsync(Http.Candidates(remote.Url,
                $"https://bmclapi2.bangbang93.com/version/{id}/json"), token);
            await File.WriteAllBytesAsync(jsonPath, versionJson, token);

            var meta = VersionResolver.Resolve(AppState.Config.GameDir, id);
            DownloadCenter.Report("下载游戏本体...", 4);

            var clientPath = Path.Combine(versionDir, meta.Jar == "" || meta.Jar == id ? id : meta.Jar + ".jar");
            if (meta.Jar != "" && meta.Jar != id)
                clientPath = Path.Combine(AppState.Config.GameDir, "versions", meta.Jar, meta.Jar + ".jar");
            else
                clientPath = Path.Combine(versionDir, id + ".jar");

            if (meta.Client != null && !Utils.HashMatches(clientPath, meta.Client.Sha1))
            {
                var ok = await Http.DownloadAsync(Http.Candidates(meta.Client.Url,
                    $"https://bmclapi2.bangbang93.com/version/{id}/client"), clientPath, meta.Client.Sha1, token);
                if (!ok) throw new IOException("游戏本体下载失败");
            }
            else if (meta.Client == null)
            {
                var ok = await Http.DownloadAsync(new[]
                {
                    $"https://bmclapi2.bangbang93.com/version/{id}/client",
                    remote.Url.Replace("/json", "/client")
                }, clientPath, null, token);
                if (!ok) throw new IOException("游戏本体下载失败");
            }

            DownloadCenter.Report("下载依赖库...", 10);
            await EnsureLibrariesAsync(meta, token, 10, 35);
            DownloadCenter.Report("下载资源文件...", 35);
            if (installAssets) await EnsureAssetsAsync(meta, token, 35, 92);
            await EnsureLoggingAsync(meta, token);
            await ExtractNativesAsync(meta, token);

            DownloadCenter.Complete("安装完成");
            Logger.Info($"已安装版本 {id}");
            await AppState.RefreshVersionsAsync();
        }
        catch (OperationCanceledException)
        {
            DownloadCenter.Fail("安装已取消");
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error($"安装 {id} 失败: {ex}");
            DownloadCenter.Fail(ex.Message);
            throw;
        }
    }

    public static async Task EnsureLibrariesAsync(VersionMeta meta, CancellationToken token,
        double progressFrom = 0, double progressTo = 60)
    {
        var tasks = new List<(string path, DownloadFile file, string url)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lib in meta.Libraries)
        {
            if (!RuleEvaluator.IsAllowed(lib.Rules)) continue;

            var artifact = lib.Artifact;
            if (artifact != null || (!string.IsNullOrEmpty(lib.Url) && lib.Url.Contains("maven")))
            {
                var path = Path.Combine(AppState.Config.GameDir, "libraries", lib.MavenPath);
                var url = artifact?.Url ?? BuildMavenUrl(lib.Url!, lib.MavenPath);
                if (!string.IsNullOrEmpty(url) && seen.Add(path))
                    tasks.Add((path, artifact ?? new DownloadFile { Url = url }, url));
            }

            var nativeClassifier = lib.WindowsNativeClassifier;
            if (nativeClassifier != null && lib.Classifiers.TryGetValue(nativeClassifier, out var classifierFile))
            {
                var classifierPath = ClassifierPath(lib, nativeClassifier);
                if (seen.Add(classifierPath))
                    tasks.Add((classifierPath, classifierFile, classifierFile.Url));
            }
        }

        var missing = tasks.Where(t => !FileExistsValid(t.path, t.file.Sha1)).ToList();
        if (missing.Count == 0) return;

        var done = 0;
        using var semaphore = new SemaphoreSlim(Math.Max(1, AppState.Config.DownloadThreads));
        var failures = new List<string>();
        var lockObj = new object();
        var downloadTasks = missing.Select(async task =>
        {
            await semaphore.WaitAsync(token);
            try
            {
                var urls = new List<string> { task.url };
                urls.AddRange(Http.Candidates(task.url));
                var ok = await Http.DownloadAsync(urls.Distinct().ToArray(), task.path, task.file.Sha1, token);
                if (!ok) lock (lockObj) failures.Add(task.path);
                var current = Interlocked.Increment(ref done);
                if (current % 3 == 0 || current == missing.Count)
                {
                    var percent = progressFrom + (progressTo - progressFrom) * current / missing.Count;
                    DownloadCenter.Report($"依赖库 {current}/{missing.Count}", percent);
                }
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();
        await Task.WhenAll(downloadTasks);
        if (failures.Count > 0)
            throw new IOException($"有 {failures.Count} 个依赖库下载失败，请检查网络");
    }

    public static async Task EnsureAssetsAsync(VersionMeta meta, CancellationToken token,
        double progressFrom = 35, double progressTo = 95)
    {
        if (meta.AssetIndex == null) return;
        var assetsRoot = Path.Combine(AppState.Config.GameDir, "assets");
        var indexDir = Path.Combine(assetsRoot, "indexes");
        Directory.CreateDirectory(indexDir);
        var indexPath = Path.Combine(indexDir, meta.AssetIndex.Id + ".json");

        if (!Utils.HashMatches(indexPath, meta.AssetIndex.Sha1))
        {
            DownloadCenter.Report("下载资源索引...", progressFrom, true);
            var urls = new List<string>();
            if (!string.IsNullOrEmpty(meta.AssetIndex.Url)) urls.Add(meta.AssetIndex.Url);
            urls.Add($"https://bmclapi2.bangbang93.com/assets/indexes/{meta.AssetIndex.Id}.json");
            var mapped = Http.Candidates(meta.AssetIndex.Url ?? "");
            urls.AddRange(mapped);
            var ok = await Http.DownloadAsync(urls.Distinct().ToArray(), indexPath, meta.AssetIndex.Sha1, token);
            if (!ok)
            {
                Logger.Error("资源索引下载失败，跳过资源文件");
                return;
            }
        }

        var indexText = await File.ReadAllTextAsync(indexPath, token);
        using var doc = JsonDocument.Parse(indexText);
        var objects = new List<(string name, string hash, long size)>();
        if (doc.RootElement.TryGetProperty("objects", out var obj) && obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (!prop.Value.TryGetProperty("hash", out var hashProp)) continue;
                var hash = hashProp.GetString() ?? "";
                var size = prop.Value.TryGetProperty("size", out var sizeProp) ? sizeProp.GetInt64() : 0;
                if (hash.Length >= 2) objects.Add((prop.Name, hash, size));
            }
        }

        var objectsRoot = Path.Combine(assetsRoot, "objects");
        var missing = new List<(string name, string hash, long size)>();
        foreach (var item in objects)
        {
            var path = Path.Combine(objectsRoot, item.hash[..2], item.hash);
            if (!File.Exists(path) || (item.size > 0 && new FileInfo(path).Length != item.size))
                missing.Add(item);
        }

        if (missing.Count > 0)
        {
            var done = 0;
            using var semaphore = new SemaphoreSlim(Math.Max(2, AppState.Config.DownloadThreads));
            var failures = 0;
            var tasks = missing.Select(async item =>
            {
                await semaphore.WaitAsync(token);
                try
                {
                    var dest = Path.Combine(objectsRoot, item.hash[..2], item.hash);
                    var urls = Http.Candidates($"https://resources.download.minecraft.net/{item.hash[..2]}/{item.hash}",
                        $"https://bmclapi2.bangbang93.com/assets/{item.hash[..2]}/{item.hash}");
                    var ok = await Http.DownloadAsync(urls, dest, item.hash, token);
                    if (!ok) Interlocked.Increment(ref failures);
                    var current = Interlocked.Increment(ref done);
                    if (current % 20 == 0 || current == missing.Count)
                    {
                        var percent = progressFrom + (progressTo - progressFrom) * current / missing.Count;
                        DownloadCenter.Report($"资源文件 {current}/{missing.Count}", percent);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();
            await Task.WhenAll(tasks);
            if (failures > 0)
                Logger.Error($"有 {failures} 个资源文件下载失败，游戏可能出现无声音/无材质");
        }

        await BuildLegacyAssetsAsync(meta, assetsRoot, indexText, token);
    }

    private static async Task BuildLegacyAssetsAsync(VersionMeta meta, string assetsRoot, string indexText, CancellationToken token)
    {
        if (!string.Equals(meta.Assets, "legacy", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(meta.AssetIndex?.Id, "legacy", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            using var doc = JsonDocument.Parse(indexText);
            if (!doc.RootElement.TryGetProperty("objects", out var objects)) return;
            var virtualDir = Path.Combine(assetsRoot, "virtual", "legacy");
            foreach (var prop in objects.EnumerateObject())
            {
                if (!prop.Value.TryGetProperty("hash", out var hashProp)) continue;
                var hash = hashProp.GetString() ?? "";
                if (hash.Length < 2) continue;
                var source = Path.Combine(assetsRoot, "objects", hash[..2], hash);
                var dest = Path.Combine(virtualDir, prop.Name.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(source)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? virtualDir);
                if (!File.Exists(dest)) File.Copy(source, dest);
            }
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Logger.Error("构建旧版资源目录失败: " + ex.Message);
        }
    }

    public static async Task EnsureLoggingAsync(VersionMeta meta, CancellationToken token)
    {
        if (meta.Logging?.File == null || string.IsNullOrEmpty(meta.Logging.File.Url)) return;
        var dir = Path.Combine(AppState.Config.GameDir, "assets", "log_configs");
        Directory.CreateDirectory(dir);
        var fileName = Path.GetFileName(new Uri(meta.Logging.File.Url).AbsolutePath);
        if (string.IsNullOrEmpty(fileName)) fileName = "client-log.xml";
        var path = Path.Combine(dir, fileName);
        if (Utils.HashMatches(path, meta.Logging.File.Sha1)) return;
        DownloadCenter.Report("下载日志配置...", 95);
        await Http.DownloadAsync(Http.Candidates(meta.Logging.File.Url), path, meta.Logging.File.Sha1, token);
    }

    public static async Task ExtractNativesAsync(VersionMeta meta, CancellationToken token)
    {
        var nativesDir = Path.Combine(meta.Folder, "natives");
        Directory.CreateDirectory(nativesDir);
        foreach (var file in Directory.GetFiles(nativesDir))
        {
            try { File.Delete(file); } catch { }
        }

        foreach (var lib in meta.Libraries)
        {
            if (!RuleEvaluator.IsAllowed(lib.Rules)) continue;
            var jarPath = "";
            var classifier = lib.WindowsNativeClassifier;
            if (classifier != null && lib.Classifiers.ContainsKey(classifier))
                jarPath = ClassifierPath(lib, classifier);
            else if (lib.Artifact != null && lib.Name.Contains(":natives-", StringComparison.OrdinalIgnoreCase))
                jarPath = Path.Combine(AppState.Config.GameDir, "libraries", lib.MavenPath);

            if (string.IsNullOrEmpty(jarPath) || !File.Exists(jarPath)) continue;
            await Task.Run(() => ExtractZip(jarPath, nativesDir), token);
        }
    }

    private static void ExtractZip(string zipPath, string destination)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                if (entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)) continue;
                var target = Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
                entry.ExtractToFile(target, true);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("解压原生库失败 " + zipPath + ": " + ex.Message);
        }
    }

    public static string ClassifierPath(Library lib, string classifier)
    {
        var parts = lib.Name.Split(':');
        if (parts.Length < 3) return Path.Combine(AppState.Config.GameDir, "libraries", lib.MavenPath);
        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var fileName = $"{artifact}-{version}-{classifier}.jar";
        return Path.Combine(AppState.Config.GameDir, "libraries", group, artifact, version, fileName);
    }

    public static string ArtifactPath(Library lib) =>
        Path.Combine(AppState.Config.GameDir, "libraries", lib.MavenPath);

    public static string BuildMavenUrl(string baseUrl, string mavenPath) =>
        baseUrl.TrimEnd('/') + "/" + mavenPath;

    private static bool FileExistsValid(string path, string? sha1)
    {
        if (!File.Exists(path)) return false;
        return string.IsNullOrWhiteSpace(sha1) || Utils.HashMatches(path, sha1);
    }

    public static async Task<string> InstallFabricAsync(string mcVersion, string? loaderVersion,
        CancellationToken token = default)
    {
        DownloadCenter.Begin("安装 Fabric Loader");
        try
        {
            DownloadCenter.Report("获取 Fabric 版本列表...", 2, true);
            var loader = loaderVersion;
            using (var doc = JsonDocument.Parse(await Http.GetStringAsync(
                       new[] { $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}" }, token)))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                    throw new InvalidOperationException("该游戏版本没有可用的 Fabric Loader");
                if (string.IsNullOrEmpty(loader))
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("loader", out var loaderObj) &&
                            loaderObj.TryGetProperty("stable", out var stable) && stable.ValueKind == JsonValueKind.True)
                        {
                            loader = loaderObj.TryGetProperty("version", out var v) ? v.GetString() : null;
                            break;
                        }
                    }
                    loader ??= doc.RootElement[0].GetProperty("loader").GetProperty("version").GetString();
                }
            }
            if (string.IsNullOrEmpty(loader)) throw new InvalidOperationException("无法确定 Fabric Loader 版本");

            var vanillaDir = Path.Combine(AppState.Config.GameDir, "versions", mcVersion);
            if (!File.Exists(Path.Combine(vanillaDir, mcVersion + ".json")))
            {
                DownloadCenter.Report("安装依赖的原版游戏...", 5, true);
                await InstallVanillaAsync(mcVersion, AppState.Config.AutoInstallAssets, token);
            }

            DownloadCenter.Report("获取 Fabric 配置...", 8, true);
            var profileJson = await Http.GetStringAsync(
                new[] { $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{loader}/profile/json" }, token);
            using var profile = JsonDocument.Parse(profileJson);
            var id = profile.RootElement.TryGetProperty("id", out var idProp)
                ? idProp.GetString() ?? $"fabric-loader-{loader}-{mcVersion}"
                : $"fabric-loader-{loader}-{mcVersion}";
            var folder = Path.Combine(AppState.Config.GameDir, "versions", id);
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, id + ".json"), profileJson, token);

            var libraries = new List<(string path, string url, string? sha1)>();
            if (profile.RootElement.TryGetProperty("libraries", out var libs) && libs.ValueKind == JsonValueKind.Array)
            {
                foreach (var lib in libs.EnumerateArray())
                {
                    var name = lib.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(name)) continue;
                    var baseUrl = lib.TryGetProperty("url", out var u) ? u.GetString() : null;
                    var sha1 = lib.TryGetProperty("sha1", out var s) ? s.GetString() : null;
                    var libObj = new Library { Name = name };
                    var path = Path.Combine(AppState.Config.GameDir, "libraries", libObj.MavenPath);
                    var url = string.IsNullOrEmpty(baseUrl)
                        ? $"https://maven.fabricmc.net/{libObj.MavenPath}"
                        : BuildMavenUrl(baseUrl, libObj.MavenPath);
                    libraries.Add((path, url, sha1));
                }
            }

            var done = 0;
            using var semaphore = new SemaphoreSlim(Math.Max(2, AppState.Config.DownloadThreads));
            await Task.WhenAll(libraries.Select(async item =>
            {
                await semaphore.WaitAsync(token);
                try
                {
                    var urls = new List<string> { item.url };
                    urls.AddRange(Http.Candidates(item.url));
                    await Http.DownloadAsync(urls.Distinct().ToArray(), item.path, item.sha1, token);
                    var current = Interlocked.Increment(ref done);
                    DownloadCenter.Report($"Fabric 依赖 {current}/{libraries.Count}",
                        10 + 85.0 * current / Math.Max(1, libraries.Count));
                }
                finally { semaphore.Release(); }
            }));

            DownloadCenter.Complete("Fabric 安装完成");
            await AppState.RefreshVersionsAsync();
            return id;
        }
        catch (OperationCanceledException)
        {
            DownloadCenter.Fail("安装已取消");
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error("安装 Fabric 失败: " + ex);
            DownloadCenter.Fail(ex.Message);
            throw;
        }
    }
}
