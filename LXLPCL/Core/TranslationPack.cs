using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace LXLPCL.Core;

/// <summary>用户手动填写的一条汉化。</summary>
public readonly record struct LangOverride(string Namespace, string Key, string Text);

/// <summary>
/// 保存用户补全的汉化条目，并生成资源包形式的汉化补丁。
/// 补丁输出到版本的 resourcepacks/LXLPCL汉化.zip，游戏内启用后覆盖模组自带的 zh_cn.json。
/// </summary>
public static class TranslationPack
{
    public static string PackFileName => "LXLPCL汉化.zip";
    private static string StoreDir => Path.Combine(AppState.DataDir, "translations");

    /// <summary>汉化补丁放在对应版本的 resourcepacks 目录（独立目录的整合包各自一份）。</summary>
    public static string PackPathFor(GameVersion version)
    {
        var root = Path.Combine(AppState.Config.GameDir, "versions", version.Id);
        var dir = version.UsesIsolatedDir
            ? Path.Combine(root, "resourcepacks")
            : Path.Combine(AppState.Config.GameDir, "resourcepacks");
        return Path.Combine(dir, PackFileName);
    }

    public static Dictionary<string, string> Load(string versionId, string modKey)
    {
        var path = StorePath(versionId, modKey);
        try
        {
            if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.Ordinal);
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return data == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(data, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Logger.Error("读取汉化记录失败：" + ex.Message);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>把本次编辑合并进记录，并重新生成资源包，返回生成的补丁路径与本次写入条数。</summary>
    public static (int Count, string PackPath) Apply(GameVersion version, ModLangInfo mod,
        IEnumerable<LangOverride> edits)
    {
        var store = Load(version.Id, mod.FileName);
        var count = 0;
        foreach (var edit in edits)
        {
            var text = edit.Text.Trim();
            if (text.Length == 0) continue;
            store[edit.Namespace + "/" + edit.Key] = text;
            count++;
        }

        SaveStore(version.Id, mod.FileName, store);

        var path = PackPathFor(version);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        BuildPack(mod, store, path);
        Logger.Info($"汉化补丁已生成：{path}（本次 {count} 条）");
        return (count, path);
    }

    /// <summary>把已保存的汉化合并进扫描结果，使列表显示补全后的覆盖率。</summary>
    public static ModLangInfo ApplySavedOverrides(GameVersion version, ModLangInfo info)
    {
        if (info.Error != null || info.Entries.Count == 0) return info;
        var saved = Load(version.Id, info.FileName);
        if (saved.Count == 0) return info;

        var entries = info.Entries.Select(entry =>
        {
            if (!saved.TryGetValue(entry.Namespace + "/" + entry.Key, out var text)
                || string.IsNullOrWhiteSpace(text)) return entry;
            return new LangEntry
            {
                Namespace = entry.Namespace,
                Key = entry.Key,
                Source = entry.Source,
                Translation = text,
                Missing = false,
                SameAsSource = string.Equals(text, entry.Source, StringComparison.Ordinal)
            };
        }).OrderBy(entry => entry.Missing ? 0 : entry.SameAsSource ? 1 : 2)
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ModLangInfo
        {
            JarPath = info.JarPath,
            FileName = info.FileName,
            DisplayName = info.DisplayName,
            ModId = info.ModId,
            Version = info.Version,
            TotalKeys = entries.Count,
            ChineseKeys = entries.Count(entry => !entry.Missing),
            MissingKeys = entries.Count(entry => entry.Missing),
            SameKeys = entries.Count(entry => entry.SameAsSource),
            SourceFile = info.SourceFile,
            ChineseFile = info.ChineseFile,
            Entries = entries,
            ChineseBase = info.ChineseBase,
            ExtraSources = info.ExtraSources
        };
    }

    private static void SaveStore(string versionId, string modKey, Dictionary<string, string> data)
    {
        try
        {
            var path = StorePath(versionId, modKey);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(data, ConfigStore.JsonOptions);
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Copy(temp, path, true);
            File.Delete(temp);
        }
        catch (Exception ex)
        {
            Logger.Error("保存汉化记录失败：" + ex.Message);
        }
    }

    private static string StorePath(string versionId, string modKey) =>
        Path.Combine(StoreDir, Sanitize(versionId), Sanitize(modKey) + ".json");

    private static string Sanitize(string name)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        return name;
    }

    /// <summary>在原补丁基础上更新该模组的命名空间，保留其它模组已生成的条目。</summary>
    private static void BuildPack(ModLangInfo mod, Dictionary<string, string> store, string path)
    {
        var entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(path))
        {
            try
            {
                using var existing = ZipFile.OpenRead(path);
                foreach (var entry in existing.Entries)
                {
                    using var stream = entry.Open();
                    using var buffer = new MemoryStream();
                    stream.CopyTo(buffer);
                    entries[entry.FullName] = buffer.ToArray();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("读取已有汉化补丁失败，将重新生成：" + ex.Message);
                entries.Clear();
            }
        }

        // 按命名空间整理：模组自带的中文 + 用户填写
        var namespaces = new HashSet<string>(mod.ChineseBase.Keys, StringComparer.OrdinalIgnoreCase);
        var grouped = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (compound, text) in store)
        {
            var index = compound.IndexOf('/');
            if (index <= 0) continue;
            var ns = compound[..index];
            var key = compound[(index + 1)..];
            namespaces.Add(ns);
            if (!grouped.TryGetValue(ns, out var dict))
            {
                dict = new Dictionary<string, string>(StringComparer.Ordinal);
                grouped[ns] = dict;
            }
            dict[key] = text;
        }

        foreach (var ns in namespaces)
        {
            var data = new Dictionary<string, string>(StringComparer.Ordinal);
            if (mod.ChineseBase.TryGetValue(ns, out var baseLang))
            {
                foreach (var (key, value) in baseLang) data[key] = value;
            }
            if (grouped.TryGetValue(ns, out var overrides))
            {
                foreach (var (key, value) in overrides) data[key] = value;
            }
            if (data.Count == 0) continue;

            var json = JsonSerializer.Serialize(data, ConfigStore.JsonOptions);
            entries[$"assets/{ns}/lang/zh_cn.json"] = System.Text.Encoding.UTF8.GetBytes(json);
        }

        const string meta = """
            {
              "pack": {
                "pack_format": 15,
                "supported_formats": [1, 99],
                "description": "LXLPCL 汉化补丁（手动补全，请在游戏内启用）"
              }
            }
            """;
        entries["pack.mcmeta"] = System.Text.Encoding.UTF8.GetBytes(meta);

        var temp = path + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var (name, bytes) in entries)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var stream = entry.Open();
                stream.Write(bytes);
            }
        }
        if (File.Exists(path)) File.Delete(path);
        File.Move(temp, path);
    }
}
