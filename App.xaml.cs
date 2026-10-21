using System;
using System.Threading;
using System.Windows;
using WindowMark.Core;

namespace WindowMark;

public partial class App : Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, @"Local\WindowMark.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            MessageBox.Show("WindowMark 已在运行，请在系统托盘中查看。", "WindowMark",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        _ownsMutex = true;

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("未处理的界面异常", args.Exception);
            args.Handled = true;
        };

        try
        {
            _controller = new AppController();
        }
        catch (Exception ex)
        {
            Log.Error("启动失败", ex);
            MessageBox.Show("WindowMark 启动失败：" + ex.Message, "WindowMark",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _controller = null;
        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); } catch { }
        }
        _mutex?.Dispose();
        _mutex = null;
        base.OnExit(e);
    }
}
