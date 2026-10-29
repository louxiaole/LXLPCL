using System.Windows;
using System.Windows.Controls;
using LXLPCL.Core;

namespace LXLPCL.UI.Controls;

/// <summary>按 URL 异步加载并缓存的图片控件，加载完成前不显示任何内容。</summary>
public class AsyncImage : Image
{
    public static readonly DependencyProperty UrlProperty = DependencyProperty.Register(
        nameof(Url), typeof(string), typeof(AsyncImage),
        new PropertyMetadata("", OnUrlChanged));

    private int _version;

    public string Url
    {
        get => (string)GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    private static async void OnUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not AsyncImage image) return;
        var version = ++image._version;
        image.Source = null;

        var url = e.NewValue as string;
        if (string.IsNullOrWhiteSpace(url)) return;

        var bitmap = await ImageLoader.LoadAsync(url);
        if (bitmap != null && image._version == version) image.Source = bitmap;
    }
}
