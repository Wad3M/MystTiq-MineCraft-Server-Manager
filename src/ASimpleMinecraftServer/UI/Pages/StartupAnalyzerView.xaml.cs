using ASimpleMinecraftServer.Models;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace ASimpleMinecraftServer.UI.Pages;

public partial class StartupAnalyzerView : UserControl
{
    private ServerProfile? _profile;
    private readonly ObservableCollection<StartupMetric> _items = new();
    public StartupAnalyzerView(){ InitializeComponent(); MetricsGrid.ItemsSource = _items; }
    public void Bind(ServerProfile? profile){ _profile=profile; AnalyzeButton.IsEnabled=profile is not null; SummaryText.Text=profile is null?"Select a server first.":$"Analyze the latest startup log for {profile.Name}."; }
    private void Analyze_Click(object sender, RoutedEventArgs e)
    {
        _items.Clear(); if(_profile is null) return;
        var log=Path.Combine(_profile.Folder,"logs","latest.log");
        if(!File.Exists(log)){ SummaryText.Text="No logs/latest.log file was found. Start the server once, then analyze again."; StartupTimeText.Text="Unavailable"; WarningText.Text="0"; return; }
        var lines=File.ReadAllLines(log); var warningCount=lines.Count(l=>l.Contains("WARN",StringComparison.OrdinalIgnoreCase)||l.Contains("ERROR",StringComparison.OrdinalIgnoreCase));
        double? doneSeconds=null; var done=lines.LastOrDefault(l=>l.Contains("Done (",StringComparison.OrdinalIgnoreCase));
        if(done is not null){ var m=Regex.Match(done,@"Done \(([0-9.,]+)s\)"); if(m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var sec)) doneSeconds=sec; }
        StartupTimeText.Text=doneSeconds is null?"Not reported":$"{doneSeconds:0.00} seconds"; WarningText.Text=warningCount.ToString();
        Add("Java and bootstrap", lines.FirstOrDefault() ?? "Startup log opened", "Observed");
        Add("Plugin loading", $"{lines.Count(l=>l.Contains("plugin",StringComparison.OrdinalIgnoreCase))} plugin-related log entries", warningCount>0?"Review":"Healthy");
        Add("World loading", lines.LastOrDefault(l=>l.Contains("Preparing spawn",StringComparison.OrdinalIgnoreCase)) ?? "No spawn timing marker found", "Observed");
        Add("Ready", done ?? "No Done marker found", doneSeconds is null?"Review":doneSeconds>30?"Slow":"Healthy", doneSeconds is null?"—":$"{doneSeconds:0.00}s");
        SummaryText.Text=$"Analyzed {lines.Length:N0} log lines. Slow startup threshold is 30 seconds.";
    }
    private void Add(string stage,string detail,string status,string duration="—")=>_items.Add(new StartupMetric{Stage=stage,Detail=detail,Status=status,Duration=duration});
}
