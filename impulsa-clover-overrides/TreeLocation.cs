using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ImpulsaExplorer.Models;

public sealed class TreeLocation : INotifyPropertyChanged
{
    private ImageSource? _iconSource;

    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public Symbol Symbol { get; set; } = Symbol.Folder;

    public ImageSource? IconSource
    {
        get => _iconSource;
        set
        {
            if (ReferenceEquals(_iconSource, value)) return;
            _iconSource = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconSource)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => Name;
}
