using System.Windows;
using System.Windows.Input;

namespace LXLPCL.UI.Windows;

/// <summary>通用文本输入弹窗，用于给皮肤等对象命名。</summary>
public partial class InputDialog : Window
{
    public InputDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) DialogResult = false;
        };
    }

    public string Value => ValueBox.Text.Trim();

    /// <summary>返回用户输入的名字；点取消或只输入空白时返回 null。</summary>
    public static string? Show(Window? owner, string title, string hint, string initial = "")
    {
        var dialog = new InputDialog();
        if (owner != null && owner.IsLoaded) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.TitleText.Text = title;
        dialog.HintText.Text = hint;
        dialog.ValueBox.Text = initial;
        if (dialog.ShowDialog() != true) return null;
        return dialog.Value.Length == 0 ? null : dialog.Value;
    }

    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch { }
    }

    private void ValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        DialogResult = true;
        e.Handled = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
