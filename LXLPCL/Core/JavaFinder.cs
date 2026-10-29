using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LXLPCL.Core;

public class JavaInfo
{
    public string Path { get; set; } = "";
    public string Home { get; set; } = "";
    public string Version { get; set; } = "";
    public int Major { get; set; }
    public bool Is64Bit { get; set; }
    public string Source { get; set; } = "系统";

    public string Display => $"Java {Major}  ·  {(Is64Bit ? "64 位" : "32 位")}  ·  {Version}";
}

public static class JavaFinder
{
    public static List<JavaInfo> Scan(string gameDir, string configuredPath = "")
    {
        var homes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddHomeFromPath(homes, configuredPath);
        AddHomeFromPath(homes, Environment.GetEnvironmentVariable("JAVA_HOME") ?? "");

        foreach (var baseDir in GetCommonDirectories())
            AddSubDirectories(homes, baseDir);

        AddSubDirectories(homes, Path.Combine(gameDir, "runtime"));
        AddSubDirectories(homes, AppState.RuntimeDir);
        FindJavawUnder(homes, Path.Combine(gameDir, "runtime"), 5);
        FindJavawUnder(homes, AppState.RuntimeDir, 5);

        foreach (var registryHome in ReadRegistryHomes()) homes.Add(registryHome);

        var result = new List<JavaInfo>();
        foreach (var home in homes)
        {
            try
            {
                var info = Inspect(home);
                if (info != null && result.All(j => !j.Home.Equals(info.Home, StringComparison.OrdinalIgnoreCase)))
                    result.Add(info);
            }
            catch (Exception ex)
            {
                Logger.Error($"检测 Java 失败 ({home}): {ex.Message}");
            }
        }

        return result
            .OrderByDescending(j => j.Major)
            .ThenByDescending(j => j.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static JavaInfo? Inspect(string homeOrExe)
    {
        var home = homeOrExe;
        if (File.Exists(home))
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(home) ?? "");
            if (dir.Name.Equals("bin", StringComparison.OrdinalIgnoreCase))
                home = dir.Parent?.FullName ?? home;
        }
        else if (home.EndsWith("javaw.exe", StringComparison.OrdinalIgnoreCase) ||
                 home.EndsWith("java.exe", StringComparison.OrdinalIgnoreCase))
        {
            home = Path.Combine(Path.GetDirectoryName(home) ?? "", "..");
            home = Path.GetFullPath(home);
        }

        if (!Directory.Exists(home)) return null;
        var javaw = Path.Combine(home, "bin", "javaw.exe");
        var java = Path.Combine(home, "bin", "java.exe");
        var exe = File.Exists(javaw) ? javaw : File.Exists(java) ? java : null;
        if (exe == null) return null;

        var version = "";
        var major = 0;
        var is64 = Environment.Is64BitOperatingSystem;
        var release = Path.Combine(home, "release");
        if (File.Exists(release))
        {
            string text;
            try { text = File.ReadAllText(release); }
            catch { text = ""; }
            var match = Regex.Match(text, "JAVA_VERSION=\"([^\"]+)\"");
            if (match.Success) version = match.Groups[1].Value;
            var arch = Regex.Match(text, "OS_ARCH=\"([^\"]+)\"");
            if (arch.Success) is64 = arch.Groups[1].Value.Contains("64");
            if (string.IsNullOrEmpty(version))
            {
                var spec = Regex.Match(text, "JAVA_VERSION_DATE=\"([^\"]+)\"");
                if (spec.Success) version = spec.Groups[1].Value;
            }
        }

        if (string.IsNullOrEmpty(version))
        {
            var (reported, is64Bit) = RunVersion(exe);
            version = reported;
            if (is64Bit.HasValue) is64 = is64Bit.Value;
        }

        major = ParseMajor(version);
        if (major == 0) return null;
        if (version == "") version = major.ToString();

        var source = "系统";
        if (home.StartsWith(Path.Combine(AppState.Config.GameDir, "runtime"), StringComparison.OrdinalIgnoreCase))
            source = "游戏运行时";
        else if (home.StartsWith(AppState.RuntimeDir, StringComparison.OrdinalIgnoreCase))
            source = "启动器下载";

        return new JavaInfo
        {
            Path = exe,
            Home = Path.GetFullPath(home),
            Version = version,
            Major = major,
            Is64Bit = is64,
            Source = source
        };
    }

    public static int ParseMajor(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return 0;
        var cleaned = version.Trim();
        if (cleaned.StartsWith("1."))
        {
            var parts = cleaned.Split('.');
            return parts.Length > 1 && int.TryParse(parts[1], out var legacy) ? legacy : 0;
        }
        var match = Regex.Match(cleaned, @"^(\d+)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var major) ? major : 0;
    }

    private static (string version, bool? is64) RunVersion(string exe)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return ("", null);
            var output = process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            var match = Regex.Match(output, "version \"([^\"]+)\"");
            var version = match.Success ? match.Groups[1].Value : "";
            bool? is64 = output.Contains("64-Bit") ? true : output.Contains("32-Bit") ? false : null;
            return (version, is64);
        }
        catch
        {
            return ("", null);
        }
    }

    private static IEnumerable<string> GetCommonDirectories()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        var names = new[]
        {
            "Java", "Eclipse Adoptium", "Microsoft", "Zulu", "BellSoft", "Amazon Corretto",
            "Semeru", "Temurin", "JetBrains", "JetBrainsRuntime", "AdoptOpenJDK", "SapMachine"
        };
        foreach (var name in names)
        {
            yield return Path.Combine(programFiles, name);
            yield return Path.Combine(programFilesX86, name);
        }
        yield return Path.Combine(localAppData, "Programs", "Eclipse Adoptium");
        yield return Path.Combine(localAppData, "Programs", "Microsoft");
        yield return Path.Combine(localAppData, "Programs", "Zulu");
        yield return Path.Combine(localAppData, "Programs", "BellSoft");
        yield return Path.Combine(programData, "Oracle", "Java");
        yield return Path.Combine(programFiles, "Java");
    }

    private static void AddSubDirectories(HashSet<string> homes, string baseDir)
    {
        try
        {
            if (!Directory.Exists(baseDir)) return;
            if (File.Exists(Path.Combine(baseDir, "bin", "javaw.exe")) ||
                File.Exists(Path.Combine(baseDir, "bin", "java.exe")))
                homes.Add(baseDir);
            foreach (var dir in Directory.GetDirectories(baseDir))
            {
                if (File.Exists(Path.Combine(dir, "bin", "javaw.exe")) ||
                    File.Exists(Path.Combine(dir, "bin", "java.exe")))
                    homes.Add(dir);
            }
        }
        catch { }
    }

    private static void FindJavawUnder(HashSet<string> homes, string root, int depth)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = depth,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (var exe in Directory.EnumerateFiles(root, "javaw.exe", options).Take(20))
            {
                var bin = Path.GetDirectoryName(exe);
                if (bin != null && Path.GetFileName(bin).Equals("bin", StringComparison.OrdinalIgnoreCase))
                    homes.Add(Directory.GetParent(bin)?.FullName ?? "");
            }
        }
        catch { }
    }

    private static IEnumerable<string> ReadRegistryHomes()
    {
        var results = new List<string>();
        var softwareKeys = new[]
        {
            @"JavaSoft\Java Runtime Environment",
            @"JavaSoft\Java Development Kit",
            @"JavaSoft\JRE",
            @"JavaSoft\JDK",
            @"Eclipse Adoptium\JDK",
            @"Eclipse Adoptium\JRE",
            @"Microsoft\JDK",
            @"Azul Systems\Zulu",
            @"BellSoft\LibericaJDK",
            @"Amazon Corretto"
        };
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var key in softwareKeys)
            {
                foreach (var wow in new[] { "", @"WOW6432Node\" })
                {
                    try
                    {
                        using var baseKey = hive.OpenSubKey(@"SOFTWARE\" + wow + key);
                        if (baseKey == null) continue;
                        foreach (var sub in baseKey.GetSubKeyNames())
                        {
                            using var subKey = baseKey.OpenSubKey(sub);
                            foreach (var valueName in new[] { "JavaHome", "InstallationPath", "Path", "Home" })
                            {
                                var value = subKey?.GetValue(valueName) as string;
                                if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
                                    results.Add(value);
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        return results;
    }

    public static JavaInfo? PickFor(int requiredMajor)
    {
        var list = AppState.Javas;
        var config = AppState.Config;
        if (requiredMajor <= 0)
            return list.OrderByDescending(j => j.Major).ThenByDescending(j => j.Is64Bit).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(config.JavaPath))
        {
            var manual = list.FirstOrDefault(j => j.Path.Equals(config.JavaPath, StringComparison.OrdinalIgnoreCase));
            if (manual != null && manual.Major >= requiredMajor) return manual;
        }
        return list.Where(j => j.Major == requiredMajor).OrderByDescending(j => j.Is64Bit).FirstOrDefault()
               ?? list.Where(j => j.Major > requiredMajor).OrderBy(j => j.Major).FirstOrDefault()
               ?? list.Where(j => j.Major >= (requiredMajor >= 17 ? 17 : requiredMajor)).OrderByDescending(j => j.Major).FirstOrDefault();
    }

    public static void AddHomeFromPath(HashSet<string> homes, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path))
            {
                var info = Inspect(path);
                if (info != null) homes.Add(info.Home);
            }
            else if (Directory.Exists(path))
            {
                homes.Add(path);
            }
        }
        catch { }
    }
}
