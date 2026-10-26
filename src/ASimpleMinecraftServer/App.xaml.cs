using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ASimpleMinecraftServer;

public partial class App : Application
{
    // One MystTiq per Windows user: a second launch just brings the first window forward.
    private const string InstanceName = "MystTiq-MineCraft-Server-Manager";
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;

    private static string StartupLogPath => Path.Combine(AppContext.BaseDirectory, "MystTiq_startup.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        try
        {
            WriteStartupLog("MystTiq application startup beginning.");
            base.OnStartup(e);

            var user = Environment.UserName;
            _instanceMutex = new Mutex(initiallyOwned: true, $@"Local\{InstanceName}-{user}", out var firstInstance);
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{InstanceName}-{user}-show");
            if (!firstInstance)
            {
                _showSignal.Set();
                WriteStartupLog("MystTiq is already running; asked it to show its window.");
                Shutdown(0);
                return;
            }

            var startHidden = Array.Exists(e.Args, arg => arg.Equals(Services.WindowsStartup.MinimizedArgument, StringComparison.OrdinalIgnoreCase));
            var shell = new PrototypeWindow { StartHidden = startHidden };
            if (startHidden)
            {
                shell.WindowState = WindowState.Minimized;
                shell.ShowInTaskbar = false;
            }
            MainWindow = shell;
            shell.Show();

            var signal = _showSignal;
            var listener = new Thread(() =>
            {
                try { while (signal.WaitOne()) Dispatcher.BeginInvoke(new Action(shell.ShowFromTray)); }
                catch (ObjectDisposedException) { }
            }) { IsBackground = true, Name = "MystTiq show-window listener" };
            listener.Start();
            WriteStartupLog("MystTiq polished shell displayed.");
        }
        catch (Exception ex)
        {
            WriteStartupLog("FATAL startup exception", ex);
            MessageBox.Show(
                $"MystTiq could not start.\n\n{ex.Message}\n\nDetails were written to:\n{StartupLogPath}",
                "MystTiq startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showSignal?.Dispose();
        try { _instanceMutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteStartupLog("Unhandled UI exception", e.Exception);
        MessageBox.Show(
            $"MystTiq encountered an unexpected error but will remain open when possible.\n\n{e.Exception.Message}\n\nDetails were written to:\n{StartupLogPath}",
            "MystTiq error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        WriteStartupLog("Unhandled application-domain exception", e.ExceptionObject as Exception);
    }

    internal static void WriteStartupLog(string message, Exception? ex = null)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTimeOffset.Now.ToString("O")).Append("] ").AppendLine(message);
            if (ex is not null) sb.AppendLine(ex.ToString());
            File.AppendAllText(StartupLogPath, sb.ToString());
        }
        catch
        {
            // Startup logging must never become a second startup failure.
        }
    }
}
