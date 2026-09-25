using System.Windows;
using System.Windows.Controls;

namespace ASimpleMinecraftServer.UI.Dialogs;

public sealed class DialogService
{
    private readonly Window _owner;

    public DialogService(Window owner) => _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public void Information(string message, string title) =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Warning(string message, string title) =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void Error(string message, string title) =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message, string title, MessageBoxImage image = MessageBoxImage.Warning) =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.YesNo, image) == MessageBoxResult.Yes;

    public string? PromptText(string title, string prompt, string initialValue)
    {
        var box = new TextBox
        {
            Text = initialValue,
            MinWidth = 330,
            Margin = new Thickness(0, 8, 0, 12)
        };

        var ok = new Button
        {
            Content = "OK",
            Width = 90,
            IsDefault = true,
            Style = (Style)_owner.FindResource("BlueActionButton")
        };

        var cancel = new Button
        {
            Content = "Cancel",
            Width = 90,
            IsCancel = true,
            Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)_owner.FindResource("SecondaryButton")
        };

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = prompt, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = title,
            Content = panel,
            Owner = _owner,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };

        ok.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };

        return window.ShowDialog() == true ? box.Text.Trim() : null;
    }
}
