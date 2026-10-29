using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LXLPCL.Core;

/// <summary>Modrinth 的项目介绍只有英文原文，这里把介绍机翻成中文，并按项目缓存到本地。</summary>
public static class ModTranslator
{
    /// <summary>单次请求允许的最大字符数，接口限制约 500 字节。</summary>
    private const int MaxChunk = 450;

    /// <summary>单个项目最多翻译的字符数，控制免费接口的额度消耗。</summary>
    private const int MaxChars = 2000;

    private static readonly Dictionary<string, string> Memo = new();

    /// <summary>判断文本是否本身就以中文为主，是的话不需要翻译。</summary>
    public static bool HasChinese(string text)
    {
        var count = 0;
        foreach (var c in text)
            if (c >= 0x4E00 && c <= 0x9FFF) count++;
        return count >= 6 && count * 8 >= text.Length;
    }

    public static string? TryGetCached(string projectId, string source)
    {
        var key = Hash(source);
        if (string.IsNullOrEmpty(projectId)) return null;
        if (Memo.TryGetValue(projectId + key, out var memo)) return memo;
        try
        {
            var path = CachePath(projectId);
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.GetProperty("src").GetString() != key) return null;
            var text = doc.RootElement.GetProperty("zh").GetString();
            if (string.IsNullOrWhiteSpace(text)) return null;
            Memo[projectId + key] = text;
            return text;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<string> TranslateAsync(string projectId, string source,
        CancellationToken token = default)
    {
        var cached = TryGetCached(projectId, source);
        if (cached != null) return cached;

        var text = source.Length > MaxChars ? source[..MaxChars] : source;
        var result = new StringBuilder();
        foreach (var chunk in Split(text))
        {
            token.ThrowIfCancellationRequested();
            if (result.Length > 0) result.Append('\n');
            result.Append(await TranslateChunkAsync(chunk, token));
            await Task.Delay(120, token);
        }

        var translated = result.ToString().Trim();
        if (source.Length > MaxChars) translated += "\n…";
        if (translated.Length > 0) SaveCache(projectId, source, translated);
        return translated;
    }

    private static async Task<string> TranslateChunkAsync(string text, CancellationToken token)
    {
        var url = "https://api.mymemory.translated.net/get?langpair=" +
                  Uri.EscapeDataString("en|zh-CN") + "&q=" + Uri.EscapeDataString(text);
        var json = await Http.Client.GetStringAsync(url, token);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("quotaFinished", out var quota) && quota.ValueKind == JsonValueKind.True)
            throw new HttpRequestException("翻译接口今日额度已用完");
        if (root.TryGetProperty("responseStatus", out var status))
        {
            var ok = status.ValueKind switch
            {
                JsonValueKind.Number => status.GetInt32() == 200,
                JsonValueKind.String => status.GetString() == "200",
                _ => false
            };
            if (!ok) throw new HttpRequestException("翻译接口返回状态 " + status);
        }
        if (!root.TryGetProperty("responseData", out var data) ||
            !data.TryGetProperty("translatedText", out var value))
            throw new HttpRequestException("翻译接口返回格式异常");

        var result = WebUtility.HtmlDecode(value.GetString() ?? "").Trim();
        // 翻译记忆库偶尔把 mod 译成“改装配件”，统一成玩家习惯的“模组”
        return result.Replace("改装配件", "模组");
    }

    /// <summary>按段落切分，超长段落再按句子边界切，保证每次请求不超过接口上限。</summary>
    private static List<string> Split(string text)
    {
        var chunks = new List<string>();
        var builder = new StringBuilder();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                if (builder.Length > 0)
                {
                    chunks.Add(builder.ToString());
                    builder.Clear();
                }
                continue;
            }
            foreach (var unit in SplitLong(line))
            {
                if (builder.Length > 0 && builder.Length + unit.Length + 1 > MaxChunk)
                {
                    chunks.Add(builder.ToString());
                    builder.Clear();
                }
                if (builder.Length > 0) builder.Append(' ');
                builder.Append(unit);
            }
        }
        if (builder.Length > 0) chunks.Add(builder.ToString());
        return chunks;
    }

    private static IEnumerable<string> SplitLong(string line)
    {
        var start = 0;
        while (start < line.Length)
        {
            var length = Math.Min(MaxChunk, line.Length - start);
            if (start + length < line.Length)
            {
                var cut = line.LastIndexOfAny(new[] { '.', '!', '?', ';' }, start + length - 1, length);
                if (cut > start + 60) length = cut - start + 1;
            }
            yield return line.Substring(start, length).Trim();
            start += length;
        }
    }

    private static string CachePath(string projectId) =>
        Path.Combine(AppState.CacheDir, "translate", Utils.SafeFileName(projectId) + ".json");

    private static void SaveCache(string projectId, string source, string chinese)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(AppState.CacheDir, "translate"));
            var payload = JsonSerializer.Serialize(new { src = Hash(source), zh = chinese });
            File.WriteAllText(CachePath(projectId), payload, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
}
