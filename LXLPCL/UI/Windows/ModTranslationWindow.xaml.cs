using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LXLPCL.Core;

namespace LXLPCL.UI.Windows;

/// <summary>汉化详情列表中的一行，汉化内容可直接编辑。</summary>
public sealed class TranslationRow : INotifyPropertyChanged
{
    private string _text = "";

    public string Namespace { get; init; } = "";
    public string Key { get; init; } = "";
    public string Source { get; init; } = "";
    /// <summary>打开窗口时已有的汉化（含之前保存过的内容），用于判断是否被修改。</summary>
    public string Original { get; init; } = "";

    public string TranslationText
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            Raise(nameof(TranslationText));
            Raise(nameof(StatusText));
            Raise(nameof(TranslationBrush));
            Raise(nameof(StatusBrush));
            Raise(nameof(StatusBackground));
            Raise(nameof(Edited));
            Raise(nameof(PlaceholderVisibility));
        }
    }

    /// <summary>汉化为空时在输入框里显示提示文字。</summary>
    public Visibility PlaceholderVisibility
        => string.IsNullOrWhiteSpace(_text) ? Visibility.Visible : Visibility.Collapsed;

    public bool Edited => !string.Equals(_text.Trim(), Original, StringComparison.Ordinal);

    public string StatusText => string.IsNullOrWhiteSpace(_text)
        ? "缺失"
        : string.Equals(_text.Trim(), Source, StringComparison.Ordinal) ? "同原文" : "已汉化";

    public Brush TranslationBrush => string.IsNullOrWhiteSpace(_text) || !Edited
        ? BrushOf("TextPrimaryBrush")
        : BrushOf("AccentTextBrush");

    public Brush StatusBrush => string.IsNullOrWhiteSpace(_text)
        ? BrushOf("DangerBrush")
        : string.Equals(_text.Trim(), Source, StringComparison.Ordinal) ? BrushOf("WarningBrush") : BrushOf("SuccessBrush");

    public Brush StatusBackground => string.IsNullOrWhiteSpace(_text)
        ? BrushOf("DangerLightBrush")
        : string.Equals(_text.Trim(), Source, StringComparison.Ordinal) ? BrushOf("WarningLightBrush") : BrushOf("SuccessLightBrush");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static Brush BrushOf(string key)
    {
        try
        {
            if (Application.Current?.Resources[key] is Brush brush) return brush;
        }
        catch
        {
            // 主题资源缺失时退化为默认颜色。
        }
        return Brushes.Gray;
    }
}

/// <summary>「其它文本来源」列表中的一行（手册页 / 指南篇 / 任务）。</summary>
public sealed class ExtraTextRow
{
    public string Type { get; init; } = "";
    public string Name { get; init; } = "";
    public string Coverage { get; init; } = "";
    public string StatusText { get; init; } = "";
    public Brush StatusBrush { get; init; } = Brushes.Gray;
    public Brush StatusBackground { get; init; } = Brushes.Transparent;
}

public partial class ModTranslationWindow : Window
{
    private readonly ModLangInfo _info;
    private readonly GameVersion? _version;
    private readonly List<TranslationRow> _rows;
    private string _filter = "all";

    /// <summary>本次打开是否保存过汉化，用于让调用方刷新列表。</summary>
    public bool Saved { get; private set; }

    public ModTranslationWindow(ModLangInfo info, GameVersion? version = null)
    {
        InitializeComponent();
        _info = info;
        _version = version;

        ModNameText.Text = info.DisplayName;
        ModSubText.Text = string.IsNullOrWhiteSpace(info.Version)
            ? info.FileName
            : $"{info.Version}  ·  {info.FileName}";
        ModSubText.ToolTip = info.JarPath;

        StatText.Text = info.HasSource
            ? $"共 {info.TotalKeys} 条  ·  已汉化 {info.ChineseKeys}  ·  缺失 {info.MissingKeys}  ·  同原文 {info.SameKeys}"
            : "";

        _rows = BuildRows(info, version);
        HintText.Text = BuildHint(info);
        ShowExtraSources(info, version);

        var unavailable = info.Error != null || !info.HasSource;
        EntryList.Visibility = unavailable ? Visibility.Collapsed : Visibility.Visible;
        FilterPanel.Visibility = unavailable ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.IsEnabled = !unavailable;

        if (info.Error != null)
        {
            ShowEmpty("读取失败", info.Error);
        }
        else if (!info.HasSource)
        {
            ShowEmpty("没有找到语言文件",
                "该模组的 jar 内没有 assets/<命名空间>/lang/en_us.json，无法对照原文与汉化。");
        }
        else
        {
            EmptyPanel.Visibility = Visibility.Collapsed;
            if (!info.HasChinese)
                HintText.Text = "无内置汉化，全部条目为缺失，可直接填写并保存生成汉化补丁";
            ApplyFilter();
        }
    }

    private static List<TranslationRow> BuildRows(ModLangInfo info, GameVersion? version)
    {
        var saved = version == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : TranslationPack.Load(version.Id, info.FileName);

        var rows = new List<TranslationRow>(info.Entries.Count);
        foreach (var entry in info.Entries)
        {
            var current = string.IsNullOrWhiteSpace(entry.Translation) ? "" : entry.Translation;
            if (saved.TryGetValue(entry.Namespace + "/" + entry.Key, out var text) && !string.IsNullOrWhiteSpace(text))
                current = text;

            rows.Add(new TranslationRow
            {
                Namespace = entry.Namespace,
                Key = entry.Key,
                Source = entry.Source,
                Original = current,
                TranslationText = current
            });
        }
        return rows;
    }

    private static string BuildHint(ModLangInfo info)
    {
        if (info.Error != null || !info.HasSource) return "";
        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(info.SourceFile)) parts.Add("原文：" + info.SourceFile);
        parts.Add(string.IsNullOrEmpty(info.ChineseFile) ? "汉化：无" : "汉化：" + info.ChineseFile);
        return string.Join("　|　", parts);
    }

    private void ShowExtraSources(ModLangInfo info, GameVersion? version)
    {
        const int maxRows = 200;
        var rows = new List<ExtraTextRow>();

        foreach (var source in info.ExtraSources)
        {
            if (source.Files.Count == 0)
            {
                rows.Add(new ExtraTextRow
                {
                    Type = source.Type,
                    Name = source.Detail,
                    Coverage = source.CoverageText,
                    StatusText = source.Chinese == 0 ? "缺失"
                        : source.Chinese >= source.Total ? "已汉化" : "部分汉化",
                    StatusBrush = source.Chinese == 0 ? TranslationRow.BrushOf("DangerBrush")
                        : source.Chinese >= source.Total ? TranslationRow.BrushOf("SuccessBrush")
                        : TranslationRow.BrushOf("WarningBrush"),
                    StatusBackground = source.Chinese == 0 ? TranslationRow.BrushOf("DangerLightBrush")
                        : source.Chinese >= source.Total ? TranslationRow.BrushOf("SuccessLightBrush")
                        : TranslationRow.BrushOf("WarningLightBrush")
                });
                continue;
            }

            foreach (var file in source.Files.Take(maxRows))
            {
                rows.Add(new ExtraTextRow
                {
                    Type = $"{source.Type}（{source.Detail}）",
                    Name = file.Name,
                    Coverage = "",
                    StatusText = file.StatusText,
                    StatusBrush = file.HasChinese ? TranslationRow.BrushOf("SuccessBrush")
                        : TranslationRow.BrushOf("DangerBrush"),
                    StatusBackground = file.HasChinese ? TranslationRow.BrushOf("SuccessLightBrush")
                        : TranslationRow.BrushOf("DangerLightBrush")
                });
            }
            if (source.Files.Count > maxRows)
                rows.Add(new ExtraTextRow
                {
                    Type = source.Type,
                    Name = $"还有 {source.Files.Count - maxRows} 个文件未显示",
                    Coverage = "",
                    StatusText = "",
                    StatusBrush = TranslationRow.BrushOf("TextTertiaryBrush")
                });
        }

        // FTB 任务来自整合包配置目录，不属于任何模组。
        var ftb = version == null ? null : ModLocalization.ScanFtbQuests(version);
        if (ftb != null)
        {
            rows.Add(new ExtraTextRow
            {
                Type = ftb.Type,
                Name = ftb.Detail,
                Coverage = ftb.CoverageText,
                StatusText = ftb.Chinese == 0 ? "缺失"
                    : ftb.Chinese >= ftb.Total ? "已汉化" : "部分汉化",
                StatusBrush = ftb.Chinese == 0 ? TranslationRow.BrushOf("DangerBrush")
                    : ftb.Chinese >= ftb.Total ? TranslationRow.BrushOf("SuccessBrush")
                    : TranslationRow.BrushOf("WarningBrush"),
                StatusBackground = ftb.Chinese == 0 ? TranslationRow.BrushOf("DangerLightBrush")
                    : ftb.Chinese >= ftb.Total ? TranslationRow.BrushOf("SuccessLightBrush")
                    : TranslationRow.BrushOf("WarningLightBrush")
            });
        }

        if (rows.Count == 0) return;

        ExtraTitle.Text = $"其它文本来源（{rows.Count} 项，不在 mod 的 lang 文件里）";
        ExtraList.ItemsSource = rows;
        ExtraPanel.Visibility = Visibility.Visible;
    }

    private void ShowEmpty(string title, string detail)
    {
        EmptyTitle.Text = title;
        EmptyDetail.Text = detail;
        EmptyPanel.Visibility = Visibility.Visible;
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (EntryList == null) return;
        if (sender is not System.Windows.Controls.RadioButton { Tag: string tag }) return;
        _filter = tag;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (!_info.HasSource) return;
        IEnumerable<TranslationRow> query = _filter switch
        {
            "missing" => _rows.Where(row => string.IsNullOrWhiteSpace(row.TranslationText)),
            "same" => _rows.Where(row => !string.IsNullOrWhiteSpace(row.TranslationText)
                                         && string.Equals(row.TranslationText.Trim(), row.Source, StringComparison.Ordinal)),
            _ => _rows
        };
        var list = query.ToList();
        EntryList.ItemsSource = list;
        if (list.Count == 0)
        {
            ShowEmpty("当前筛选下没有条目",
                _filter == "missing" ? "该模组的汉化没有缺失条目。" : "没有与原文相同的条目。");
        }
        else
        {
            EmptyPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_version == null)
        {
            MessageDialog.Show(this, "无法保存", "请先在启动器里选择对应的游戏版本。", warning: true);
            return;
        }

        var edits = _rows
            .Where(row => row.Edited && !string.IsNullOrWhiteSpace(row.TranslationText))
            .Select(row => new LangOverride(row.Namespace, row.Key, row.TranslationText))
            .ToList();

        if (edits.Count == 0)
        {
            MessageDialog.Show(this, "没有修改", "还没有填写或修改任何汉化内容。", okText: "好的");
            return;
        }

        try
        {
            var (count, path) = TranslationPack.Apply(_version, _info, edits);
            var refreshed = BuildRows(TranslationPack.ApplySavedOverrides(_version, _info), _version);
            _rows.Clear();
            _rows.AddRange(refreshed);
            ApplyFilter();
            Saved = true;

            MessageDialog.Show(this, "汉化已保存",
                $"已写入 {count} 条汉化，并生成汉化补丁：\n{path}\n\n" +
                "在游戏的「资源包」里启用 LXLPCL汉化 后才会生效（需排在模组资源之上）。",
                okText: "好的");
        }
        catch (Exception ex)
        {
            Logger.Error("保存汉化失败：" + ex);
            MessageDialog.Show(this, "保存失败", ex.Message, warning: true);
        }
    }

    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
