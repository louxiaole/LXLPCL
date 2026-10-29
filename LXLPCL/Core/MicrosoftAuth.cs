using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LXLPCL.Core;

public class DeviceCodeInfo
{
    public string DeviceCode { get; set; } = "";
    public string UserCode { get; set; } = "";
    public string VerificationUri { get; set; } = "";
    public string Message { get; set; } = "";
    public int ExpiresIn { get; set; } = 900;
    public int Interval { get; set; } = 5;
}

public class MicrosoftProfile
{
    public string Name { get; set; } = "";
    public string Uuid { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string Xuid { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}

/// <summary>微软账户登录：OAuth 设备代码流 → Xbox Live → XSTS → Minecraft Services。</summary>
public static class MicrosoftAuth
{
    /// <summary>公开的启动器客户端 ID，可在配置中替换为自己注册的 Azure 应用。</summary>
    public const string DefaultClientId = "c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb";

    private const string Scope = "XboxLive.signin offline_access";
    private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string XboxAuthUrl = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string MinecraftLoginUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string ProfileUrl = "https://api.minecraftservices.com/minecraft/profile";

    private static string ClientId =>
        string.IsNullOrWhiteSpace(AppState.Config.MicrosoftClientId)
            ? DefaultClientId
            : AppState.Config.MicrosoftClientId.Trim();

    public static async Task<DeviceCodeInfo> StartDeviceCodeAsync(CancellationToken token = default)
    {
        using var doc = await PostFormAsync(DeviceCodeUrl, new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = Scope
        }, token);
        var root = doc.RootElement;
        var info = new DeviceCodeInfo
        {
            DeviceCode = GetString(root, "device_code"),
            UserCode = GetString(root, "user_code"),
            VerificationUri = GetString(root, "verification_uri"),
            Message = GetString(root, "message"),
            ExpiresIn = GetInt(root, "expires_in", 900),
            Interval = GetInt(root, "interval", 5)
        };
        if (info.DeviceCode.Length == 0 || info.UserCode.Length == 0)
            throw new InvalidOperationException("微软未返回设备代码，请稍后重试。");
        Logger.Info($"微软登录：已获取设备代码 {info.UserCode}");
        return info;
    }

    public static async Task<MicrosoftProfile> WaitForLoginAsync(DeviceCodeInfo info, CancellationToken token = default)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(60, info.ExpiresIn));
        var interval = Math.Max(1, info.Interval);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), token);
            using var doc = await PostFormAsync(TokenUrl, new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = ClientId,
                ["device_code"] = info.DeviceCode
            }, token, allowError: true);
            var root = doc.RootElement;
            if (root.TryGetProperty("access_token", out var access))
            {
                var msaToken = access.GetString() ?? "";
                var refresh = GetString(root, "refresh_token");
                var expiresIn = GetInt(root, "expires_in", 3600);
                return await CompleteLoginAsync(msaToken, refresh, expiresIn, token);
            }

            var error = GetString(root, "error");
            switch (error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += 5;
                    continue;
                case "expired_token":
                    throw new InvalidOperationException("授权代码已过期，请重新开始登录。");
                case "authorization_declined":
                    throw new InvalidOperationException("已在浏览器中取消授权。");
                case "bad_verification_code":
                    throw new InvalidOperationException("授权代码无效，请重新开始登录。");
                default:
                    throw new InvalidOperationException(ErrorText(root, "微软登录失败"));
            }
        }
        throw new TimeoutException("等待授权超时，请重新开始登录。");
    }

    public static async Task<MicrosoftProfile> RefreshAsync(string refreshToken, CancellationToken token = default)
    {
        using var doc = await PostFormAsync(TokenUrl, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["refresh_token"] = refreshToken,
            ["scope"] = Scope
        }, token, allowError: true);
        var root = doc.RootElement;
        if (!root.TryGetProperty("access_token", out var access))
            throw new InvalidOperationException("Microsoft 登录已失效，请在账户管理中重新登录。");
        var newRefresh = GetString(root, "refresh_token");
        if (newRefresh.Length == 0) newRefresh = refreshToken;
        return await CompleteLoginAsync(access.GetString() ?? "", newRefresh,
            GetInt(root, "expires_in", 3600), token);
    }

    private static async Task<MicrosoftProfile> CompleteLoginAsync(string msaToken, string refreshToken,
        int expiresIn, CancellationToken token)
    {
        var (xblToken, uhs, xuid) = await XboxAuthenticateAsync(msaToken, token);
        var xstsToken = await XstsAuthorizeAsync(xblToken, token);
        var (mcToken, mcExpiresIn) = await MinecraftLoginAsync(xstsToken, uhs, token);
        var (uuid, name) = await GetProfileAsync(mcToken, token);
        Logger.Info($"微软登录：Minecraft 账户 {name}（{uuid}）");
        return new MicrosoftProfile
        {
            Name = name,
            Uuid = uuid,
            AccessToken = mcToken,
            RefreshToken = refreshToken,
            Xuid = xuid,
            ExpiresAt = DateTime.Now.AddSeconds(mcExpiresIn)
        };
    }

    private static async Task<(string Token, string Uhs, string Xuid)> XboxAuthenticateAsync(
        string msaToken, CancellationToken token)
    {
        var payload = new
        {
            Properties = new Dictionary<string, string>
            {
                ["AuthMethod"] = "RPS",
                ["SiteName"] = "user.auth.xboxlive.com",
                ["RpsTicket"] = "d=" + msaToken
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };
        using var doc = await PostJsonAsync(XboxAuthUrl, payload, token);
        var root = doc.RootElement;
        var ticket = GetString(root, "Token");
        if (ticket.Length == 0) throw new InvalidOperationException("Xbox Live 认证失败。");
        var xui = root.TryGetProperty("DisplayClaims", out var claims) &&
                  claims.TryGetProperty("xui", out var list) &&
                  list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0
            ? list[0]
            : default;
        return (ticket, xui.ValueKind == JsonValueKind.Object ? GetString(xui, "uhs") : "",
            xui.ValueKind == JsonValueKind.Object ? GetString(xui, "xid") : "");
    }

    private static async Task<string> XstsAuthorizeAsync(string xblToken, CancellationToken token)
    {
        var payload = new
        {
            Properties = new Dictionary<string, object>
            {
                ["SandboxId"] = "RETAIL",
                ["UserTokens"] = new[] { xblToken }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        };
        using var doc = await PostJsonAsync(XstsUrl, payload, token, allowError: true);
        var root = doc.RootElement;
        var ticket = GetString(root, "Token");
        if (ticket.Length > 0) return ticket;
        var xerr = root.TryGetProperty("XErr", out var err) && err.TryGetInt64(out var code) ? code : 0;
        throw new InvalidOperationException(xerr switch
        {
            2148916233 => "该 Microsoft 账户还没有 Xbox 档案。\n请先访问 xbox.com 创建档案后再试。",
            2148916235 => "该账户所在地区不支持 Xbox Live，无法登录。",
            2148916236 or 2148916237 => "该账户需要先在 xbox.com 完成成人身份验证。",
            2148916238 => "该账户是儿童账户，需要先加入 Microsoft 家庭组。",
            _ => "Xbox 认证失败：" + ErrorText(root, "未知错误")
        });
    }

    private static async Task<(string Token, int ExpiresIn)> MinecraftLoginAsync(string xstsToken, string uhs,
        CancellationToken token)
    {
        var payload = new { identityToken = $"XBL3.0 x={uhs};{xstsToken}" };
        using var doc = await PostJsonAsync(MinecraftLoginUrl, payload, token, allowError: true);
        var root = doc.RootElement;
        var access = GetString(root, "access_token");
        if (access.Length == 0)
            throw new InvalidOperationException("Minecraft 认证失败：" + ErrorText(root, "请确认账户已购买 Minecraft: Java Edition。"));
        return (access, GetInt(root, "expires_in", 86400));
    }

    private static async Task<(string Uuid, string Name)> GetProfileAsync(string mcToken, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ProfileUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcToken);
        using var response = await SendAsync(request, token);
        var body = await response.Content.ReadAsStringAsync(token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("该 Microsoft 账户没有 Minecraft: Java Edition 的拥有记录。");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("获取 Minecraft 档案失败：" + ErrorText(body, response.ReasonPhrase ?? ""));
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var id = GetString(root, "id");
        var name = GetString(root, "name");
        if (id.Length == 0 || name.Length == 0)
            throw new InvalidOperationException("Minecraft 档案缺少玩家信息，请稍后重试。");
        var uuid = Guid.TryParse(id, out var guid) ? guid.ToString("D") : id;
        return (uuid, name);
    }

    private static async Task<JsonDocument> PostFormAsync(string url, Dictionary<string, string> form,
        CancellationToken token, bool allowError = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var response = await SendAsync(request, token);
        var body = await response.Content.ReadAsStringAsync(token);
        var doc = JsonDocument.Parse(body);
        if (!response.IsSuccessStatusCode && !allowError)
        {
            var text = ErrorText(doc.RootElement, response.ReasonPhrase ?? "请求失败");
            doc.Dispose();
            throw new InvalidOperationException(text);
        }
        return doc;
    }

    private static async Task<JsonDocument> PostJsonAsync(string url, object payload, CancellationToken token,
        bool allowError = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        using var response = await SendAsync(request, token);
        var body = await response.Content.ReadAsStringAsync(token);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("服务器返回了无法识别的数据，请稍后重试。");
        }
        if (!response.IsSuccessStatusCode && !allowError)
        {
            var text = ErrorText(doc.RootElement, response.ReasonPhrase ?? "请求失败");
            doc.Dispose();
            throw new InvalidOperationException(text);
        }
        return doc;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        return await Http.Client.SendAsync(request, timeout.Token);
    }

    private static string ErrorText(JsonElement root, string fallback)
    {
        var description = GetString(root, "error_description");
        if (description.Length > 0) return description;
        var message = GetString(root, "errorMessage");
        if (message.Length > 0) return message;
        var error = GetString(root, "error");
        return error.Length > 0 ? error : fallback;
    }

    private static string ErrorText(string json, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ErrorText(doc.RootElement, fallback);
        }
        catch (JsonException)
        {
            return string.IsNullOrWhiteSpace(json) ? fallback : json;
        }
    }

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static int GetInt(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : fallback;
}
