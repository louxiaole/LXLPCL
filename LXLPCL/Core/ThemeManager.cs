using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace LXLPCL.Core;

public static class ThemeManager
{
    public const string ModeLight = "light";
    public const string ModeDark = "dark";
    public const string ModeMidnight = "midnight";
    public const string ModeSepia = "sepia";
    public const string ModeSystem = "system";

    public static readonly (string Key, string Name)[] Modes =
    {
        (ModeLight, "浅色"),
        (ModeDark, "深色"),
        (ModeMidnight, "午夜黑"),
        (ModeSepia, "护眼"),
        (ModeSystem, "跟随系统")
    };

    public static Color AccentColor { get; private set; } = (Color)ColorConverter.ConvertFromString("#2E7CF6");

    public const string BgNone = "none";

    public static readonly (string Key, string Name)[] Backgrounds =
    {
        (BgNone, "无背景"),
        ("sakura", "樱花夜"),
        ("starry", "极光星空"),
        ("neon", "霓虹都市"),
        ("forest", "精灵森林")
    };

    /// <summary>当前背景：none、内置主题名或本地图片路径。</summary>
    public static string CurrentBackground { get; private set; } = BgNone;

    public static event Action? BackgroundChanged;

    public static void ApplyBackground(string? value)
    {
        CurrentBackground = string.IsNullOrWhiteSpace(value) ? BgNone : value.Trim();
        UpdateGlass();
        BackgroundChanged?.Invoke();
    }

    /// <summary>把背景标识解析为图片文件路径，找不到时返回 null。</summary>
    public static string? ResolveBackgroundImage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == BgNone) return null;
        if (value.IndexOfAny(new[] { '\\', '/' }) >= 0 || value.Contains(':'))
            return File.Exists(value) ? value : null;
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Themes", value + ".jpg");
        return File.Exists(path) ? path : null;
    }

    public static string BackgroundName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == BgNone) return "无背景";
        foreach (var background in Backgrounds)
            if (background.Key == value) return background.Name;
        return "自定义图片";
    }

    /// <summary>当前应用的主题模式（可能是 system）。</summary>
    public static string CurrentMode { get; private set; } = ModeLight;

    /// <summary>当前实际生效的深色外观。</summary>
    public static bool IsDark { get; private set; }

    private sealed record Palette(
        string Window, string Surface, string SurfaceAlt, string Sidebar, string InputBackground,
        string InputBorder, string Border, string BorderStrong, string Hover, string Pressed,
        string TextPrimary, string TextSecondary, string TextTertiary);

    private static Palette? _palette;

    private static readonly Palette Light = new(
        "#F4F6F9", "#FFFFFF", "#F9FAFC", "#FBFBFC", "#F6F7F9",
        "#E1E5EB", "#E7EAF0", "#D8DDE5", "#F0F2F6", "#E5E9EF",
        "#1C212B", "#5F6875", "#87909C");

    private static readonly Palette Dark = new(
        "#17191F", "#1E2129", "#232733", "#1B1E25", "#262B35",
        "#363C48", "#2C323D", "#3C4351", "#2A2F3A", "#333A47",
        "#E6E9EF", "#A4ADBC", "#7C8698");

    private static readonly Palette Midnight = new(
        "#0A0B0E", "#13151A", "#171A21", "#0E1014", "#1C2029",
        "#2B3140", "#22262F", "#333947", "#202530", "#2A303C",
        "#EEF0F4", "#A2ABBB", "#79839A");

    private static readonly Palette Sepia = new(
        "#EDE4D2", "#FBF6E9", "#F6EFDF", "#F8F2E4", "#F3EBD9",
        "#DDD2BF", "#E4DAC7", "#CDC2AC", "#F2EADA", "#E8DFCC",
        "#3A3123", "#6B6250", "#877C68");

    /// <summary>应用界面主题（浅色 / 深色 / 午夜黑 / 护眼 / 跟随系统）。</summary>
    public static void ApplyTheme(string? mode)
    {
        mode = (mode ?? "").Trim().ToLowerInvariant();
        if (mode is not (ModeLight or ModeDark or ModeMidnight or ModeSepia or ModeSystem)) mode = ModeLight;
        CurrentMode = mode;

        var effective = mode == ModeSystem ? (SystemPrefersDark() ? ModeDark : ModeLight) : mode;
        IsDark = effective is ModeDark or ModeMidnight;
        var palette = effective switch
        {
            ModeDark => Dark,
            ModeMidnight => Midnight,
            ModeSepia => Sepia,
            _ => Light
        };

        if (Application.Current == null) return;
        var resources = Application.Current.Resources;
        _palette = palette;
        SetBrush(resources, "WindowBackgroundBrush", palette.Window);
        SetBrush(resources, "SurfaceBrush", palette.Surface);
        SetBrush(resources, "SurfaceAltBrush", palette.SurfaceAlt);
        SetBrush(resources, "SidebarBrush", palette.Sidebar);
        SetBrush(resources, "InputBackgroundBrush", palette.InputBackground);
        SetBrush(resources, "InputBorderBrush", palette.InputBorder);
        SetBrush(resources, "BorderBrush", palette.Border);
        SetBrush(resources, "BorderStrongBrush", palette.BorderStrong);
        SetBrush(resources, "HoverBrush", palette.Hover);
        SetBrush(resources, "PressedBrush", palette.Pressed);
        SetBrush(resources, "TextPrimaryBrush", palette.TextPrimary);
        SetBrush(resources, "TextSecondaryBrush", palette.TextSecondary);
        SetBrush(resources, "TextTertiaryBrush", palette.TextTertiary);
        UpdateGlass();
        BackgroundChanged?.Invoke();
    }

    /// <summary>启用背景图片时卡片改为半透明，让插画透出来。</summary>
    private static void UpdateGlass()
    {
        if (Application.Current == null || _palette == null) return;
        var hasBackground = ResolveBackgroundImage(CurrentBackground) != null;
        var surface = ParseColor(_palette.Surface);
        var glass = hasBackground
            ? Color.FromArgb((byte)(IsDark ? 0xDA : 0xC6), surface.R, surface.G, surface.B)
            : surface;
        SetBrush(Application.Current.Resources, "SurfaceGlassBrush", glass);

        var mask = !hasBackground
            ? Color.FromArgb(0, 255, 255, 255)
            : IsDark
                ? Color.FromArgb(0x9A, 4, 6, 10)
                : Color.FromArgb(0x66, 255, 255, 255);
        SetBrush(Application.Current.Resources, "MaskBrush", mask);
    }

    private static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void SetBrush(ResourceDictionary resources, string key, string hex) =>
        SetBrush(resources, key, ParseColor(hex));

    private static void SetBrush(ResourceDictionary resources, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[key] = brush;
    }

    public static void Apply(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) hex = "#2E7CF6";
        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(hex); }
        catch { color = (Color)ColorConverter.ConvertFromString("#2E7CF6"); }
        AccentColor = color;
        if (Application.Current == null) return;

        var resources = Application.Current.Resources;
        resources["AccentColor"] = color;
        resources["AccentBrush"] = Brush(color);
        resources["AccentDarkBrush"] = Brush(Shade(color, -0.16));
        resources["AccentDeepBrush"] = Brush(Shade(color, -0.3));
        resources["AccentHoverBrush"] = Brush(Shade(color, 0.12));
        resources["AccentLightBrush"] = Brush(Color.FromArgb((byte)(IsDark ? 40 : 28), color.R, color.G, color.B));
        resources["AccentLighterBrush"] = Brush(Color.FromArgb((byte)(IsDark ? 24 : 16), color.R, color.G, color.B));
        resources["AccentTextBrush"] = Brush(IsDark ? Shade(color, 0.28) : Shade(color, -0.18));
        resources["AccentGradientBrush"] = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(Shade(color, 0.1), 0),
                new GradientStop(Shade(color, -0.18), 1)
            }
        };
    }

    public static Color Shade(Color color, double factor)
    {
        double Adjust(byte v) => factor >= 0 ? v + (255 - v) * factor : v * (1 + factor);
        return Color.FromRgb(
            (byte)Math.Clamp(Adjust(color.R), 0, 255),
            (byte)Math.Clamp(Adjust(color.G), 0, 255),
            (byte)Math.Clamp(Adjust(color.B), 0, 255));
    }

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
