using ASimpleMinecraftServer.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ASimpleMinecraftServer.UI.Pages;

public partial class ConfigurationView : UserControl
{
    private readonly ObservableCollection<ServerPropertyEntry> _properties = new();
    private readonly ObservableCollection<ServerPropertyEntry> _filtered = new();
    private ServerProfile? _profile;
    private bool _loaded;
    public event EventHandler<ServerProfile>? ConfigurationSaved;
    public ConfigurationView() { InitializeComponent(); AdvancedPropertiesGrid.ItemsSource = _filtered; }
    public void ResetForSelection(ServerProfile? profile) { if (!ReferenceEquals(_profile, profile)) _loaded=false; _profile=profile; LoadConfiguration(); }
    public void LoadConfiguration(bool force=false)
    {
        ConfigurationServerText.Text=_profile is null?"Select a server to edit its server.properties file.":$"Editing {_profile.Name} • server.properties"; ConfigurationStatusText.Text=string.Empty;
        if(_profile is null){_properties.Clear();_filtered.Clear();_loaded=false;return;} if(_loaded&&!force)return;
        var path=Path.Combine(_profile.Folder,"server.properties"); _properties.Clear();
        if(!File.Exists(path)) ConfigurationStatusText.Text="server.properties was not found. Start the server once or save to create it.";
        else { foreach(var raw in File.ReadAllLines(path)){var line=raw.Trim();if(line.Length==0||line.StartsWith('#'))continue;var split=line.IndexOf('=');if(split<1)continue;_properties.Add(new(line[..split].Trim(),line[(split+1)..]));} ConfigurationStatusText.Text=$"Loaded {_properties.Count} settings.";}
        EnsureDefaults(); SyncToSimple(); ApplyFilter(); _loaded=true;
    }
    private void SimpleConfig_Click(object s,RoutedEventArgs e){SimpleConfigPanel.Visibility=Visibility.Visible;AdvancedConfigPanel.Visibility=Visibility.Collapsed;SimpleConfigButton.Style=(Style)FindResource("GreenActionButton");AdvancedConfigButton.Style=(Style)FindResource("SecondaryButton");}
    private void AdvancedConfig_Click(object s,RoutedEventArgs e){SyncFromSimple();SimpleConfigPanel.Visibility=Visibility.Collapsed;AdvancedConfigPanel.Visibility=Visibility.Visible;SimpleConfigButton.Style=(Style)FindResource("SecondaryButton");AdvancedConfigButton.Style=(Style)FindResource("GreenActionButton");ApplyFilter();}
    private void ReloadConfiguration_Click(object s,RoutedEventArgs e)=>LoadConfiguration(true);
    private void ConfigurationSearchBox_TextChanged(object s,TextChangedEventArgs e)=>ApplyFilter();
    private void SaveConfiguration_Click(object s,RoutedEventArgs e)
    {
        if(_profile is null){MessageBox.Show("Select a server first.","Server Configuration",MessageBoxButton.OK,MessageBoxImage.Information);return;} SyncFromSimple();
        if(!int.TryParse(Get("server-port"),out var port)||port is <1 or >65535){SetValidation("Server port must be between 1 and 65535.");return;}
        if(!int.TryParse(Get("max-players"),out var players)||players is <1 or >1000){SetValidation("Maximum players must be between 1 and 1000.");return;}
        try{Directory.CreateDirectory(_profile.Folder);var path=Path.Combine(_profile.Folder,"server.properties");var existing=File.Exists(path)?File.ReadAllLines(path).ToList():new List<string>();var written=new HashSet<string>(StringComparer.OrdinalIgnoreCase);for(int i=0;i<existing.Count;i++){var line=existing[i];var split=line.IndexOf('=');if(split<1||line.TrimStart().StartsWith('#'))continue;var key=line[..split].Trim();var entry=Find(key);if(entry is null)continue;existing[i]=$"{entry.Key}={entry.Value}";written.Add(entry.Key);}foreach(var entry in _properties.Where(e=>!written.Contains(e.Key)).OrderBy(e=>e.Key))existing.Add($"{entry.Key}={entry.Value}");File.WriteAllLines(path,existing);_profile.RefreshDerivedProperties();ConfigurationStatusText.Text=$"Saved {_properties.Count} settings to server.properties.";ConfigurationSaved?.Invoke(this,_profile);}
        catch(Exception ex){SetValidation(ex.Message,true);}
    }
    private void SetValidation(string message,bool error=false){ConfigurationStatusText.Text=message;MessageBox.Show(message,error?"Could not save configuration":"Invalid setting",MessageBoxButton.OK,error?MessageBoxImage.Error:MessageBoxImage.Warning);}
    private void EnsureDefaults(){var d=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{"motd","A Minecraft Server"},{"max-players","20"},{"server-port","25565"},{"gamemode","survival"},{"difficulty","easy"},{"pvp","true"},{"online-mode","true"},{"hardcore","false"},{"allow-flight","false"},{"allow-nether","true"},{"level-seed",""},{"spawn-protection","16"},{"view-distance","10"},{"simulation-distance","10"},{"spawn-animals","true"},{"spawn-monsters","true"}};foreach(var p in d)if(Find(p.Key)is null)_properties.Add(new(p.Key,p.Value));}
    private ServerPropertyEntry? Find(string k)=>_properties.FirstOrDefault(p=>p.Key.Equals(k,StringComparison.OrdinalIgnoreCase)); private string Get(string k,string f="")=>Find(k)?.Value??f; private void Set(string k,string v){var p=Find(k);if(p is null)_properties.Add(new(k,v));else p.Value=v;}
    private static bool Bool(string v,bool f=false)=>bool.TryParse(v,out var r)?r:f; private static void Select(ComboBox b,string v){foreach(var i in b.Items.OfType<ComboBoxItem>())if(string.Equals(i.Content?.ToString(),v,StringComparison.OrdinalIgnoreCase)){b.SelectedItem=i;return;}if(b.Items.Count>0)b.SelectedIndex=0;} private static string Combo(ComboBox b,string f)=>(b.SelectedItem as ComboBoxItem)?.Content?.ToString()??f;
    private void SyncToSimple(){ConfigMotdBox.Text=Get("motd","A Minecraft Server");ConfigMaxPlayersBox.Text=Get("max-players","20");ConfigPortBox.Text=Get("server-port","25565");Select(ConfigGameModeCombo,Get("gamemode","survival"));Select(ConfigDifficultyCombo,Get("difficulty","easy"));ConfigPvpCheckBox.IsChecked=Bool(Get("pvp","true"));ConfigOnlineModeCheckBox.IsChecked=Bool(Get("online-mode","true"));ConfigHardcoreCheckBox.IsChecked=Bool(Get("hardcore","false"));ConfigAllowFlightCheckBox.IsChecked=Bool(Get("allow-flight","false"));ConfigAllowNetherCheckBox.IsChecked=Bool(Get("allow-nether","true"));ConfigSeedBox.Text=Get("level-seed");ConfigSpawnProtectionBox.Text=Get("spawn-protection","16");ConfigViewDistanceBox.Text=Get("view-distance","10");ConfigSimulationDistanceBox.Text=Get("simulation-distance","10");ConfigSpawnAnimalsCheckBox.IsChecked=Bool(Get("spawn-animals","true"));ConfigSpawnMonstersCheckBox.IsChecked=Bool(Get("spawn-monsters","true"));}
    private void SyncFromSimple(){if(!_loaded)return;Set("motd",ConfigMotdBox.Text);Set("max-players",ConfigMaxPlayersBox.Text.Trim());Set("server-port",ConfigPortBox.Text.Trim());Set("gamemode",Combo(ConfigGameModeCombo,"survival"));Set("difficulty",Combo(ConfigDifficultyCombo,"easy"));Set("pvp",(ConfigPvpCheckBox.IsChecked==true).ToString().ToLowerInvariant());Set("online-mode",(ConfigOnlineModeCheckBox.IsChecked==true).ToString().ToLowerInvariant());Set("hardcore",(ConfigHardcoreCheckBox.IsChecked==true).ToString().ToLowerInvariant());Set("allow-flight",(ConfigAllowFlightCheckBox.IsChecked==true).ToString().ToLowerInvariant());Set("allow-nether",(ConfigAllowNetherCheckBox.IsChecked==true).ToString().ToLowerInvariant());Set("level-seed",ConfigSeedBox.Text.Trim());Set("spawn-protection",ConfigSpawnProtectionBox.Text.Trim());Set("view-distance",ConfigViewDistanceBox.Text.Trim());Set("simulation-distance",ConfigSimulationDistanceBox.Text.Trim());Set("spawn-animals",(ConfigSpawnAnimalsCheckBox.IsChecked==true).ToString().ToLowerInvariant());Set("spawn-monsters",(ConfigSpawnMonstersCheckBox.IsChecked==true).ToString().ToLowerInvariant());}
    private void ApplyFilter(){var q=ConfigurationSearchBox.Text.Trim();_filtered.Clear();foreach(var i in _properties.Where(p=>q.Length==0||p.Key.Contains(q,StringComparison.OrdinalIgnoreCase)||p.Value.Contains(q,StringComparison.OrdinalIgnoreCase)).OrderBy(p=>p.Key))_filtered.Add(i);}
}
