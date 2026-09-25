using ASimpleMinecraftServer.Models;
using System.Windows;
using System.Windows.Controls;
namespace ASimpleMinecraftServer.UI.Pages;
public partial class NotificationCenterView : UserControl
{
 public NotificationCenterView()=>InitializeComponent();
 public event EventHandler? MarkAllRequested; public event EventHandler? MarkSelectedRequested; public event EventHandler? ClearRequested;
 public NotificationRecord? SelectedNotification => NotificationsGrid.SelectedItem as NotificationRecord;
 private void MarkAll_Click(object sender,RoutedEventArgs e)=>MarkAllRequested?.Invoke(this,EventArgs.Empty);
 private void MarkSelected_Click(object sender,RoutedEventArgs e)=>MarkSelectedRequested?.Invoke(this,EventArgs.Empty);
 private void Clear_Click(object sender,RoutedEventArgs e)=>ClearRequested?.Invoke(this,EventArgs.Empty);
}
