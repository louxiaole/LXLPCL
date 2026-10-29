using System.Windows.Controls;

namespace LXLPCL;

public class PageBase : UserControl
{
    public virtual void OnShown() { }
}

public static class Navigation
{
    public static event Action<string>? Requested;
    public static void Go(string key) => Requested?.Invoke(key);
}
