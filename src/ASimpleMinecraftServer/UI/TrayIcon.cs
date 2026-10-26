using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace ASimpleMinecraftServer.UI;

/// <summary>
/// The MystTiq icon in the Windows notification area: double-click to open, right-click for a
/// menu, and balloon tips for things like a crashed server while the window is hidden.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon? _image;

    public TrayIcon(Action open, Action exit)
    {
        _image = LoadAppIcon();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open MystTiq", null, (_, _) => open());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());
        _icon = new Forms.NotifyIcon
        {
            Icon = _image ?? System.Drawing.SystemIcons.Application,
            Text = "MystTiq Minecraft Server Manager",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => open();
        _icon.BalloonTipClicked += (_, _) => open();
    }

    /// <summary>Updates the hover text (Windows limits it to 127 characters).</summary>
    public void SetToolTip(string text) => _icon.Text = text.Length > 127 ? text[..127] : text;

    public void Notify(string title, string message, bool warning = false) =>
        _icon.ShowBalloonTip(5000, title, message, warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);

    private static System.Drawing.Icon? LoadAppIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
            if (resource is null) return null;
            using var stream = resource.Stream;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            return new System.Drawing.Icon(copy, Forms.SystemInformation.SmallIconSize);
        }
        catch (IOException) { return null; }
        catch (ArgumentException) { return null; }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image?.Dispose();
    }
}
