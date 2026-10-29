using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace LXLPCL.Core;

public sealed record DownloadProgress(long Received, long Total);

/// <summary>GitHub Release 中的一个可用更新。</summary>
public sealed class UpdateInfo
{
    public string Version { get; init; } = "";
    public string VersionName { get; init; } = "";
    public string ReleaseDate { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public long FileSize { get; init; }
    public string Changelog { get; init; } = "";
}

/// <summary>
/// 自动更新：读取 GitHub Releases 最新版本，与当前版本比较，
/// 下载整包 zip 后由批处理脚本替换文件并重启启动器。
/// 发版只需在 GitHub 创建 Release（tag=v1.0.1）并上传 zip。
/// </summary>
public static class Updater
{
    public const string Owner = "louxiaole";
    public const string Repo = "LXLPCL";

    public static string ReleasesPage => $"https://github.com/{Owner}/{Repo}/releases";
    public static string RepoPage => $"https://github.com/{Owner}/{Repo}";

    private const string ApiUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    // GitHub API 不可用时的回退（仓库根目录的 version.json）
    private static readonly string[] VersionUrls =
    {
        $"https://cdn.jsdelivr.net/gh/{Owner}/{Repo}@main/version.json",
        $"https://raw.githubusercontent.com/{Owner}/{Repo}/main/version.json"
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly HttpClient DownloadHttp = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static string TempZipPath => Path.Combine(AppState.BaseDir, "update_temp.zip");

    // ---------- 版本比较 ----------

    public static (int Major, int Minor, int Build) ParseVersion(string text)
    {
        var core = text.Trim().TrimStart('v', 'V').Split(' ')[0].Split('-')[0];
        var parts = core.Split('.');
        static int Pick(string[] list, int index) =>
            index < list.Length && int.TryParse(list[index], out var value) ? value : 0;
        return (Pick(parts, 0), Pick(parts, 1), Pick(parts, 2));
    }

    private static int Compare((int Major, int Minor, int Build) left, (int Major, int Minor, int Build) right)
    {
        if (left.Major != right.Major) return left.Major.CompareTo(right.Major);
        if (left.Minor != right.Minor) return left.Minor.CompareTo(right.Minor);
        return left.Build.CompareTo(right.Build);
    }

    // ---------- 检查更新 ----------

    /// <summary>检查是否有新版本；无更新或检查失败时返回 null。</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken token = default)
    {
        var current = ParseVersion(AppState.VersionText);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", "LXLPCL-Updater");
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            using var response = await Http.SendAsync(request, token);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(token);
                var info = ParseRelease(json, current);
                Logger.Info(info == null
                    ? "检查更新：已是最新版本 " + AppState.VersionText
                    : $"检查更新：发现新版本 {info.VersionName}");
                return info;
            }
            Logger.Info($"检查更新：GitHub API 返回 {(int)response.StatusCode}，尝试 version.json");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Info("检查更新失败（GitHub API）：" + ex.Message);
        }

        foreach (var url in VersionUrls)
        {
            try
            {
                using var response = await Http.GetAsync(url, token);
                if (!response.IsSuccessStatusCode) continue;
                var json = await response.Content.ReadAsStringAsync(token);
                return ParseVersionJson(json, current);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.Info($"检查更新失败（{url}）：{ex.Message}");
            }
        }
        return null;
    }

    private static UpdateInfo? ParseRelease(string json, (int, int, int) current)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = (root.TryGetProperty("tag_name", out var tagNode) ? tagNode.GetString() : "") ?? "";
            tag = tag.TrimStart('v', 'V');
            if (Compare(ParseVersion(tag), current) <= 0) return null;

            var url = "";
            long size = 0;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameNode) ? nameNode.GetString() : null;
                    if (string.IsNullOrEmpty(name) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        continue;
                    url = (asset.TryGetProperty("browser_download_url", out var urlNode) ? urlNode.GetString() : "") ?? "";
                    if (asset.TryGetProperty("size", out var sizeNode)) size = sizeNode.GetInt64();
                    break;
                }
            }
            if (url.Length == 0)
            {
                Logger.Info("检查更新：最新 Release 中没有 zip 附件");
                return null;
            }

            var published = (root.TryGetProperty("published_at", out var publishedNode) ? publishedNode.GetString() : "") ?? "";
            var title = (root.TryGetProperty("name", out var titleNode) ? titleNode.GetString() : "") ?? tag;
            var body = (root.TryGetProperty("body", out var bodyNode) ? bodyNode.GetString() : "") ?? "";

            return new UpdateInfo
            {
                Version = tag,
                VersionName = string.IsNullOrWhiteSpace(title) ? tag : title,
                ReleaseDate = published.Length >= 10 ? published[..10] : "",
                DownloadUrl = url,
                FileSize = size,
                Changelog = body
            };
        }
        catch (Exception ex)
        {
            Logger.Error("解析 Release 失败：" + ex.Message);
            return null;
        }
    }

    private static UpdateInfo? ParseVersionJson(string json, (int, int, int) current)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var version = (root.TryGetProperty("version", out var v) ? v.GetString() : "") ?? "";
            if (Compare(ParseVersion(version), current) <= 0) return null;

            return new UpdateInfo
            {
                Version = version,
                VersionName = (root.TryGetProperty("version_name", out var n) ? n.GetString() : "") ?? version,
                ReleaseDate = (root.TryGetProperty("release_date", out var d) ? d.GetString() : "") ?? "",
                DownloadUrl = (root.TryGetProperty("download_url", out var u) ? u.GetString() : "") ?? "",
                FileSize = root.TryGetProperty("file_size", out var s) ? s.GetInt64() : 0,
                Changelog = (root.TryGetProperty("changelog_html", out var c) ? c.GetString() : "") ?? ""
            };
        }
        catch (Exception ex)
        {
            Logger.Error("解析 version.json 失败：" + ex.Message);
            return null;
        }
    }

    // ---------- 下载与安装 ----------

    /// <summary>下载更新包到启动器目录，返回 zip 路径。</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<DownloadProgress> progress,
        CancellationToken token)
    {
        using var response = await DownloadHttp.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? info.FileSize;

        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = File.Create(TempZipPath);
        var buffer = new byte[81920];
        long received = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, token);
            if (read <= 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            received += read;
            progress.Report(new DownloadProgress(received, total));
        }
        return TempZipPath;
    }

    /// <summary>生成更新批处理并启动，随后调用方应退出进程让脚本接管。</summary>
    public static void LaunchInstaller(string zipPath)
    {
        var directory = AppState.BaseDir;
        var batPath = Path.Combine(directory, "_updater_temp.bat");
        // 批处理只用 ASCII，避免中文 Windows 下 cmd 编码问题
        File.WriteAllText(batPath, BuildBatScript(Path.GetFileName(zipPath)), Encoding.ASCII);
        Process.Start(new ProcessStartInfo
        {
            FileName = batPath,
            WorkingDirectory = directory,
            UseShellExecute = true
        });
        Logger.Info("更新脚本已启动：" + batPath);
    }

    private static string BuildBatScript(string zipName) => $"""
        @echo off
        cd /d "%~dp0"
        title LXLPCL Updater
        echo Updating LXLPCL, please wait...
        echo.

        set /a wait_count=0
        :wait_loop
        tasklist /fi "imagename eq LXLPCL.exe" 2>nul | find /i "LXLPCL.exe" >nul
        if %errorlevel%==0 (
            set /a wait_count+=1
            if %wait_count% geq 30 (
                taskkill /im LXLPCL.exe /f >nul 2>&1
                goto extract
            )
            timeout /t 1 /nobreak >nul
            goto wait_loop
        )

        :extract
        echo [1/3] Backing up config...
        if exist "LXLPCL.json" copy /y "LXLPCL.json" "LXLPCL_config_backup.json" >nul 2>&1

        echo [2/3] Extracting {zipName} ...
        powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -Force -Path '{zipName}' -DestinationPath '.'"
        if not exist "LXLPCL.exe" goto failed
        for %%A in ("LXLPCL.exe") do set exe_size=%%~zA
        if %exe_size% lss 100000 goto failed

        echo [3/3] Restoring config and starting...
        if exist "LXLPCL_config_backup.json" move /y "LXLPCL_config_backup.json" "LXLPCL.json" >nul 2>&1
        del "{zipName}" >nul 2>&1
        start "" "LXLPCL.exe"
        timeout /t 3 /nobreak >nul
        del "%~f0"
        exit

        :failed
        echo.
        echo Update failed. Please extract {zipName} to this folder manually.
        pause
        """;
}
