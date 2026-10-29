using System.Diagnostics;
using System.IO;
using System.Text;

namespace LXLPCL.Core;

public class JavaNotFoundException : Exception
{
    public int RequiredMajor { get; }
    public string? Component { get; }

    public JavaNotFoundException(int requiredMajor, string? component)
        : base($"未找到可用的 Java {requiredMajor}" + (string.IsNullOrEmpty(component) ? "" : $"（{component}）"))
    {
        RequiredMajor = requiredMajor;
        Component = component;
    }
}

public class LaunchResult
{
    public Process Process { get; set; } = null!;
    public JavaInfo Java { get; set; } = null!;
    public string GameLogPath { get; set; } = "";
}

public static class GameLauncher
{
    public static Process? RunningProcess { get; private set; }
    public static event Action<string, int, string>? GameExited;

    public static async Task<LaunchResult> LaunchAsync(GameVersion version, Account account,
        IProgress<string>? status = null, CancellationToken token = default)
    {
        if (RunningProcess is { HasExited: false })
            throw new InvalidOperationException("已有一个游戏正在运行，请等待其退出");
        if (account == null) throw new InvalidOperationException("请先选择一个账户");
        if (version.Meta == null) throw new InvalidOperationException("版本信息解析失败：" + version.Error);

        if (account.Type == "微软")
        {
            status?.Report("正在验证微软账户...");
            await AppState.EnsureMicrosoftTokenAsync(account, token);
        }

        var meta = VersionResolver.Resolve(AppState.Config.GameDir, version.Id);
        var gameDir = AppState.Config.GameDir;
        var launchDir = version.UsesIsolatedDir
            ? Path.Combine(gameDir, "versions", version.Id)
            : gameDir;
        Directory.CreateDirectory(launchDir);

        status?.Report("正在检查游戏文件...");
        DownloadCenter.Begin("检查游戏文件");
        await Installer.EnsureLibrariesAsync(meta, token, 0, 45);
        if (AppState.Config.AutoInstallAssets)
        {
            status?.Report("正在检查资源文件...");
            await Installer.EnsureAssetsAsync(meta, token, 45, 88);
        }
        await Installer.EnsureLoggingAsync(meta, token);
        await Installer.ExtractNativesAsync(meta, token);
        DownloadCenter.Complete("资源检查完成");

        status?.Report("正在查找 Java...");
        var (java, required) = ResolveJava(version, meta);
        if (java == null) throw new JavaNotFoundException(required, meta.JavaComponent);

        status?.Report("正在构建启动参数...");
        var arguments = BuildArguments(version, meta, account, java, launchDir);
        Logger.Info($"启动 {version.Id}，Java {java.Major}: {java.Path}");
        Logger.Info("启动参数: " + string.Join(" ", arguments.Select(QuoteForLog)));

        var psi = new ProcessStartInfo
        {
            FileName = java.Path,
            WorkingDirectory = launchDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);

        var logDir = Path.Combine(AppState.DataDir, "logs");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, $"game-{Utils.SafeFileName(version.Id)}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        void WriteGameLog(string? line)
        {
            if (line == null) return;
            try { File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8); } catch { }
        }

        process.OutputDataReceived += (_, e) => WriteGameLog(e.Data);
        process.ErrorDataReceived += (_, e) =>
        {
            WriteGameLog(e.Data);
            if (!string.IsNullOrEmpty(e.Data)) Logger.Write("[游戏] " + e.Data);
        };
        process.Exited += (_, _) =>
        {
            var code = 0;
            try { code = process.ExitCode; } catch { }
            Logger.Write($"游戏已退出，退出代码 {code}");
            RunningProcess = null;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            void Fire() => GameExited?.Invoke(version.Id, code, logPath);
            if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke(Fire);
            else Fire();
        };

        status?.Report("正在启动游戏...");
        if (!process.Start()) throw new InvalidOperationException("无法启动 Java 进程");
        RunningProcess = process;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        status?.Report("游戏已启动");
        DownloadCenter.Complete("游戏已启动");
        AppState.Config.LastPlayed[version.Id] = DateTime.Now;
        AppState.SaveConfig();

        await Task.CompletedTask;
        return new LaunchResult { Process = process, Java = java, GameLogPath = logPath };
    }

    public static (JavaInfo? java, int required) ResolveJava(GameVersion version, VersionMeta meta)
    {
        var required = Math.Max(8, meta.JavaMajor);
        var overrides = AppState.Config.GetVersionSettings(version.Id);
        var configured = overrides.JavaPath ?? AppState.Config.JavaPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var info = JavaFinder.Inspect(configured);
            if (info != null && info.Major >= required)
                return (info, required);
        }

        var found = JavaFinder.PickFor(required);
        return (found, required);
    }

    private static List<string> BuildArguments(GameVersion version, VersionMeta meta, Account account, JavaInfo java,
        string launchDir)
    {
        var config = AppState.Config;
        var gameDir = config.GameDir;
        var overrides = config.GetVersionSettings(version.Id);
        var memory = MemoryInfo.Resolve(overrides.MemoryMb);
        var width = overrides.WindowWidth ?? config.WindowWidth;
        var height = overrides.WindowHeight ?? config.WindowHeight;
        var fullscreen = overrides.Fullscreen ?? config.Fullscreen;

        var nativesDir = Path.Combine(meta.Folder, "natives");
        Directory.CreateDirectory(nativesDir);
        var assetsRoot = Path.Combine(gameDir, "assets");
        var legacyAssets = Path.Combine(assetsRoot, "virtual", "legacy");
        if (!Directory.Exists(legacyAssets)) legacyAssets = assetsRoot;

        var classpathParts = new List<string>();
        foreach (var lib in meta.Libraries)
        {
            if (!RuleEvaluator.IsAllowed(lib.Rules)) continue;
            if (lib.Artifact == null && string.IsNullOrEmpty(lib.Url)) continue;
            var path = Installer.ArtifactPath(lib);
            if (!File.Exists(path))
            {
                if (lib.Artifact == null) continue;
                throw new FileNotFoundException("缺少依赖库: " + lib.Name);
            }
            classpathParts.Add(path);
        }

        var jarId = !string.IsNullOrEmpty(meta.Jar)
            ? meta.Jar
            : File.Exists(Path.Combine(gameDir, "versions", meta.Id, meta.Id + ".jar"))
                ? meta.Id
                : meta.RootId;
        var jarPath = Path.Combine(gameDir, "versions", jarId, jarId + ".jar");
        if (!File.Exists(jarPath))
            throw new FileNotFoundException("缺少游戏本体: " + jarPath);
        classpathParts.Add(jarPath);
        var classpath = string.Join(";", classpathParts);

        var logFile = "";
        if (meta.Logging?.File != null && !string.IsNullOrEmpty(meta.Logging.File.Url))
        {
            var fileName = Path.GetFileName(new Uri(meta.Logging.File.Url).AbsolutePath);
            var candidate = Path.Combine(assetsRoot, "log_configs", fileName);
            if (File.Exists(candidate)) logFile = candidate;
        }

        var substitutions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = account.Name,
            ["version_name"] = meta.Id,
            ["game_directory"] = launchDir,
            ["assets_root"] = assetsRoot,
            ["game_assets"] = legacyAssets,
            ["assets_index_name"] = meta.AssetIndex?.Id ?? meta.Assets,
            ["auth_uuid"] = account.Uuid.Replace("-", ""),
            ["auth_access_token"] = string.IsNullOrEmpty(account.AccessToken) ? "0" : account.AccessToken,
            ["auth_session"] = "token:" + (account.AccessToken is { Length: > 0 } t ? t : "0") + ":" + account.Uuid.Replace("-", ""),
            ["user_type"] = account.Type == "微软" ? "msa" : "legacy",
            ["version_type"] = string.IsNullOrEmpty(meta.Type) ? "release" : meta.Type,
            ["user_properties"] = "{}",
            ["clientid"] = "",
            ["auth_xuid"] = account.Xuid,
            ["launcher_name"] = "LXLPCL",
            ["launcher_version"] = AppState.VersionText,
            ["classpath"] = classpath,
            ["classpath_separator"] = ";",
            ["library_directory"] = Path.Combine(gameDir, "libraries"),
            ["natives_directory"] = nativesDir,
            ["resolution_width"] = width.ToString(),
            ["resolution_height"] = height.ToString(),
            ["path"] = logFile,
            ["profile_name"] = account.Name
        };

        var jvmArgs = new List<string>();
        foreach (var arg in meta.JvmArgs)
        {
            if (!RuleEvaluator.IsAllowed(arg.Rules, true)) continue;
            foreach (var value in arg.Values)
                jvmArgs.Add(Substitute(value, substitutions));
        }

        if (meta.Logging != null && !string.IsNullOrEmpty(logFile))
            jvmArgs.Insert(0, Substitute(meta.Logging.Argument, substitutions));

        if (!jvmArgs.Any(a => a.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase)))
        {
            jvmArgs.Insert(0, $"-Xmx{Math.Max(512, memory)}M");
            jvmArgs.Insert(1, $"-Xms{Math.Max(256, memory / 4)}M");
        }
        if (!jvmArgs.Any(a => a.Contains("java.library.path", StringComparison.OrdinalIgnoreCase)))
            jvmArgs.Add("-Djava.library.path=" + nativesDir);
        if (!jvmArgs.Any(a => a == "-cp" || a == "-classpath" || a.Contains("${classpath}") || a == classpath))
        {
            jvmArgs.Add("-cp");
            jvmArgs.Add(classpath);
        }
        jvmArgs.Add("-Dminecraft.launcher.brand=LXLPCL");
        jvmArgs.Add("-Dminecraft.launcher.version=" + AppState.VersionText);
        jvmArgs.Add("-Dlog4j2.formatMsgNoLookups=true");

        var customJvm = overrides.JvmArgs ?? config.CustomJvmArgs;
        if (!string.IsNullOrWhiteSpace(customJvm))
            jvmArgs.AddRange(ParseCommandLine(customJvm));

        var gameArgs = new List<string>();
        if (meta.GameArgs.Count > 0)
        {
            foreach (var arg in meta.GameArgs)
            {
                if (!RuleEvaluator.IsAllowed(arg.Rules, true)) continue;
                foreach (var value in arg.Values)
                    gameArgs.Add(Substitute(value, substitutions));
            }
        }
        else if (!string.IsNullOrWhiteSpace(meta.MinecraftArguments))
        {
            foreach (var token in meta.MinecraftArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                gameArgs.Add(Substitute(token, substitutions));
        }

        if (!gameArgs.Any(a => a.Equals("--width", StringComparison.OrdinalIgnoreCase)) && !fullscreen)
        {
            gameArgs.Add("--width");
            gameArgs.Add(width.ToString());
            gameArgs.Add("--height");
            gameArgs.Add(height.ToString());
        }
        if (fullscreen) gameArgs.Add("--fullscreen");

        var customGame = overrides.GameArgs ?? config.CustomGameArgs;
        if (!string.IsNullOrWhiteSpace(customGame))
            gameArgs.AddRange(ParseCommandLine(customGame));

        var result = new List<string>();
        result.AddRange(jvmArgs);
        result.Add(meta.MainClass);
        result.AddRange(gameArgs);
        return result;
    }

    private static string Substitute(string value, Dictionary<string, string> map)
    {
        foreach (var pair in map)
            value = value.Replace("${" + pair.Key + "}", pair.Value, StringComparison.Ordinal);
        return value;
    }

    public static List<string> ParseCommandLine(string text)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var c in text)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
            }
            else current.Append(c);
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    private static string QuoteForLog(string value) =>
        value.Contains(' ') ? "\"" + value + "\"" : value;
}
