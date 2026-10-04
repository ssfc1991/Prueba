using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ImpulsaExplorer.Models;

public sealed class FileEntry : INotifyPropertyChanged
{
    private ImageSource? _iconSource;

    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public bool IsDrive { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? Modified { get; set; }
    public string TypeLabel { get; set; } = string.Empty;
    public Symbol FallbackSymbol { get; set; } = Symbol.Document;

    public ImageSource? IconSource
    {
        get => _iconSource;
        set
        {
            if (ReferenceEquals(_iconSource, value)) return;
            _iconSource = value;
            OnPropertyChanged();
        }
    }

    public string SizeDisplay => IsFolder || IsDrive || SizeBytes is null ? string.Empty : FormatSize(SizeBytes.Value);
    public string ModifiedDisplay => Modified?.ToString("dd/MM/yyyy HH:mm") ?? string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.##} {units[unit]}";
    }
}
