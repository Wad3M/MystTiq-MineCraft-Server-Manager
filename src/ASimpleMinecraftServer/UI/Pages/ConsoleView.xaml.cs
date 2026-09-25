using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ASimpleMinecraftServer.UI.Pages;

public partial class ConsoleView : UserControl
{
    public ConsoleView() => InitializeComponent();

    public TextBlock ConsoleServerText => ConsoleServerTextControl;
    public Border ConsoleLiveBadge => ConsoleLiveBadgeControl;
    public System.Windows.Shapes.Ellipse ConsoleLiveDot => ConsoleLiveDotControl;
    public TextBlock ConsoleLiveText => ConsoleLiveTextControl;
    public TextBox ConsoleSearchBox => ConsoleSearchBoxControl;
    public ComboBox ConsoleFilterCombo => ConsoleFilterComboControl;
    public TextBox ConsoleBox => ConsoleBoxControl;
    public Button PauseConsoleButton => PauseConsoleButtonControl;
    public Button JumpLatestButton => JumpLatestButtonControl;
    public CheckBox ConsoleAutoScrollCheckBox => ConsoleAutoScrollCheckBoxControl;
    public CheckBox ConsoleTimestampsCheckBox => ConsoleTimestampsCheckBoxControl;
    public TextBox CommandBox => CommandBoxControl;

    public event KeyEventHandler? SearchKeyDownRequested;
    public event RoutedEventHandler? FindPreviousRequested;
    public event RoutedEventHandler? FindNextRequested;
    public event SelectionChangedEventHandler? FilterChangedRequested;
    public event RoutedEventHandler? SaveLogRequested;
    public event RoutedEventHandler? PauseRequested;
    public event RoutedEventHandler? JumpLatestRequested;
    public event RoutedEventHandler? DisplayOptionChangedRequested;
    public event RoutedEventHandler? ClearRequested;
    public event KeyEventHandler? CommandKeyDownRequested;
    public event RoutedEventHandler? SendCommandRequested;

    private void ConsoleSearchBox_KeyDown(object sender, KeyEventArgs e) => SearchKeyDownRequested?.Invoke(sender, e);
    private void FindPreviousConsoleText_Click(object sender, RoutedEventArgs e) => FindPreviousRequested?.Invoke(sender, e);
    private void FindConsoleText_Click(object sender, RoutedEventArgs e) => FindNextRequested?.Invoke(sender, e);
    private void ConsoleFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => FilterChangedRequested?.Invoke(sender, e);
    private void SaveConsoleLog_Click(object sender, RoutedEventArgs e) => SaveLogRequested?.Invoke(sender, e);
    private void PauseConsole_Click(object sender, RoutedEventArgs e) => PauseRequested?.Invoke(sender, e);
    private void JumpLatest_Click(object sender, RoutedEventArgs e) => JumpLatestRequested?.Invoke(sender, e);
    private void ConsoleDisplayOption_Changed(object sender, RoutedEventArgs e) => DisplayOptionChangedRequested?.Invoke(sender, e);
    private void ClearConsole_Click(object sender, RoutedEventArgs e) => ClearRequested?.Invoke(sender, e);
    private void CommandBox_KeyDown(object sender, KeyEventArgs e) => CommandKeyDownRequested?.Invoke(sender, e);
    private void SendCommand_Click(object sender, RoutedEventArgs e) => SendCommandRequested?.Invoke(sender, e);
}
