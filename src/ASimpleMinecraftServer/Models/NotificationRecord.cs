using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ASimpleMinecraftServer.Models;

public sealed class NotificationRecord : INotifyPropertyChanged
{
    private bool _isRead;
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string Category { get; set; } = "Information";
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public bool IsRead { get => _isRead; set { if (_isRead == value) return; _isRead = value; OnPropertyChanged(); OnPropertyChanged(nameof(Status)); } }
    public string CreatedText => CreatedAt.LocalDateTime.ToString("g");
    public string Status => IsRead ? "Read" : "New";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
