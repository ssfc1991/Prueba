using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace ImpulsaExplorer.Controls;

public sealed partial class BrowserPane
{
    private bool _moreViewsInstalled;

    private void InstallMoreViews()
    {
        if (_moreViewsInstalled)
            return;

        _moreViewsInstalled = true;
        ViewModeComboBox.Width = 185;
        ViewModeComboBox.SelectionChanged += MoreViews_SelectionChanged;
        ViewModeComboBox.Items.Clear();

        AddViewChoice("Detalles", "Details");
        AddViewChoice("Lista", "List");
        AddViewChoice("Iconos pequeños", "SmallIcons");
        AddViewChoice("Iconos medianos", "Icons");
        AddViewChoice("Iconos grandes", "LargeIcons");
        AddViewChoice("Iconos extra grandes", "ExtraLargeIcons");
        AddViewChoice("Mosaicos", "Tiles");

        ViewModeComboBox.SelectedIndex = 0;
    }

    private void AddViewChoice(string text, string tag)
    {
        ViewModeComboBox.Items.Add(new ComboBoxItem { Content = text, Tag = tag });
    }

    private void MoreViews_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_moreViewsInstalled || ViewModeComboBox.SelectedItem is not ComboBoxItem selected)
            return;

        var tag = selected.Tag?.ToString() ?? "Details";
        switch (tag)
        {
            case "SmallIcons":
                ShowCustomIconView(88, 76, 28, 11, tiles: false);
                break;
            case "Icons":
                ShowCustomIconView(128, 112, 52, 12, tiles: false);
                break;
            case "LargeIcons":
                ShowCustomIconView(170, 148, 82, 12.5, tiles: false);
                break;
            case "ExtraLargeIcons":
                ShowCustomIconView(220, 190, 112, 13, tiles: false);
                break;
            case "Tiles":
                ShowCustomIconView(280, 76, 48, 13, tiles: true);
                break;
            default:
                // Details and List are handled by the original ExplorerPro handler.
                break;
        }
    }

    private void ShowCustomIconView(double itemWidth, double itemHeight, double iconSize, double fontSize, bool tiles)
    {
        _viewMode = ExplorerViewMode.Icons;

        DetailsHeader.Visibility = Visibility.Collapsed;
        FilesListView.Visibility = Visibility.Collapsed;
        CompactListView.Visibility = Visibility.Collapsed;
        IconsGridView.Visibility = Visibility.Visible;

        IconsGridView.ItemTemplate = BuildIconTemplate(itemWidth, itemHeight, iconSize, fontSize, tiles);
        IconsGridView.UpdateLayout();
        ConfigureItemsWrapGrid(itemWidth, itemHeight);
        RestoreSelection(FilesListView.SelectedItems.OfType<Models.FileEntry>().ToArray());
    }

    private void ConfigureItemsWrapGrid(double width, double height)
    {
        if (IconsGridView.ItemsPanelRoot is ItemsWrapGrid wrap)
        {
            wrap.ItemWidth = width;
            wrap.ItemHeight = height;
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (IconsGridView.ItemsPanelRoot is ItemsWrapGrid later)
            {
                later.ItemWidth = width;
                later.ItemHeight = height;
            }
        });
    }

    private static DataTemplate BuildIconTemplate(double itemWidth, double itemHeight, double iconSize, double fontSize, bool tiles)
    {
        string xaml;
        if (tiles)
        {
            xaml = $"""
<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
  <Grid Width="{itemWidth - 8}" Height="{itemHeight - 6}" Padding="8,5" ColumnSpacing="10">
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="{iconSize + 6}"/>
      <ColumnDefinition Width="*"/>
    </Grid.ColumnDefinitions>
    <Grid Width="{iconSize}" Height="{iconSize}" VerticalAlignment="Center">
      <SymbolIcon Symbol="{{Binding FallbackSymbol}}" Width="{iconSize}" Height="{iconSize}"/>
      <Image Source="{{Binding IconSource}}" Width="{iconSize}" Height="{iconSize}" Stretch="Uniform"/>
    </Grid>
    <StackPanel Grid.Column="1" VerticalAlignment="Center" Spacing="2">
      <TextBlock Text="{{Binding Name}}" FontSize="{fontSize}" TextTrimming="CharacterEllipsis" MaxLines="1"/>
      <TextBlock Text="{{Binding TypeLabel}}" FontSize="11" Opacity="0.68" TextTrimming="CharacterEllipsis" MaxLines="1"/>
    </StackPanel>
  </Grid>
</DataTemplate>
""";
        }
        else
        {
            xaml = $"""
<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
  <Grid Width="{itemWidth - 8}" Height="{itemHeight - 6}" Padding="5">
    <Grid.RowDefinitions>
      <RowDefinition Height="*"/>
      <RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>
    <Grid Width="{iconSize}" Height="{iconSize}" HorizontalAlignment="Center" VerticalAlignment="Center">
      <SymbolIcon Symbol="{{Binding FallbackSymbol}}" Width="{iconSize}" Height="{iconSize}"/>
      <Image Source="{{Binding IconSource}}" Width="{iconSize}" Height="{iconSize}" Stretch="Uniform"/>
    </Grid>
    <TextBlock Grid.Row="1" Text="{{Binding Name}}" FontSize="{fontSize}" TextAlignment="Center" TextWrapping="Wrap" MaxLines="2" TextTrimming="CharacterEllipsis"/>
  </Grid>
</DataTemplate>
""";
        }

        return (DataTemplate)XamlReader.Load(xaml);
    }
}
