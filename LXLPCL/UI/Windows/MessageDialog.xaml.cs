using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace LXLPCL.UI.Windows;

public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = CancelButton.Visibility == Visibility.Visible ? false : true; }
        };
    }

    public static bool Show(Window? owner, string title, string message,
        string okText = "确定", string? cancelText = null, bool warning = false)
    {
        var dialog = new MessageDialog();
        if (owner != null && owner.IsLoaded) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.OkButton.Content = okText;
        if (cancelText != null)
        {
            dialog.CancelButton.Content = cancelText;
            dialog.CancelButton.Visibility = Visibility.Visible;
        }
        if (warning)
        {
            dialog.IconGlyph.Text = "\uE7BA";
            dialog.IconGlyph.Foreground = (Brush)Application.Current.Resources["WarningBrush"];
        }
        dialog.ShowDialog();
        return dialog.DialogResult == true;
    }

    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
