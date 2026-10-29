using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LXLPCL.UI.Windows;

public partial class SkinViewerWindow : Window
{
    public SkinViewerWindow(string name, ImageSource skin, bool canUse)
    {
        InitializeComponent();
        TitleText.Text = name;
        ModelView.Skin = skin;
        UseButton.IsEnabled = canUse;
        UseButton.Content = canUse ? "使用这张皮肤" : "使用中";
    }

    public bool UseRequested { get; private set; }
    public bool EditRequested { get; private set; }

    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch { }
    }

    private void View_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        ModelView.Yaw = double.Parse(tag);
        ModelView.Pitch = 0;
    }

    private void TopView_Click(object sender, RoutedEventArgs e)
    {
        ModelView.Yaw = 0;
        ModelView.Pitch = 62;
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        UseRequested = true;
        DialogResult = true;
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        EditRequested = true;
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
