using System.Windows;
using System.Windows.Controls;
namespace ASimpleMinecraftServer.UI.Pages;
public partial class HealthAnalyzerView : UserControl
{
 public HealthAnalyzerView()=>InitializeComponent();
 public event EventHandler? AnalyzeRequested;
 private void Analyze_Click(object sender,RoutedEventArgs e)=>AnalyzeRequested?.Invoke(this,EventArgs.Empty);
}
