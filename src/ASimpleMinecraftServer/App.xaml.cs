using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace ASimpleMinecraftServer;

public partial class App : Application
{
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

            var shell = new PrototypeWindow();
            MainWindow = shell;
            shell.Show();
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
