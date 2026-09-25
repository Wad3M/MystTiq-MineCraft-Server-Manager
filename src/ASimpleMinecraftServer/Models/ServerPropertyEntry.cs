using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ASimpleMinecraftServer.Models;

public sealed class ServerPropertyEntry : INotifyPropertyChanged
{
    private string _value;
    public ServerPropertyEntry(string key, string value) { Key = key; _value = value; }
    public string Key { get; }
    public string Value { get => _value; set { if (_value == value) return; _value = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
