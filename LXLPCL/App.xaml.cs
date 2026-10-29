using System.Windows;
using System.Windows.Threading;
using LXLPCL.Core;

namespace LXLPCL;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppState.Initialize();
        Logger.Initialize();

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Logger.Error("未处理异常: " + args.ExceptionObject);
        };
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("界面线程异常: " + e.Exception);
        MessageBox.Show("启动器遇到错误：" + e.Exception.Message, "LXLPCL", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
