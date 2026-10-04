using ImpulsaExplorer.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ImpulsaExplorer.Controls;

public sealed partial class BrowserPane
{
    private readonly Dictionary<TreeViewNode, string> _treePaths = new();
    private List<FileEntry> _searchMaster = [];
    private bool _explorerProInitialized;
    private bool _syncingSearch;
    private bool _syncingSelection;
    private ExplorerViewMode _viewMode = ExplorerViewMode.Details;

    private enum ExplorerViewMode
    {
        Details,
        List,
        Icons
    }

    private void BrowserPane_Loaded(object sender, RoutedEventArgs e)
    {
        if (_explorerProInitialized) return;
        _explorerProInitialized = true;

        LocationChanged += BrowserPane_LocationChangedForSearch;
        _searchMaster = Entries.ToList();
        BuildFolderTree();
        ApplyViewMode(ExplorerViewMode.Details);
    }

    private void BrowserPane_LocationChangedForSearch(object? sender, string path)
    {
        _searchMaster = Entries.ToList();
        _syncingSearch = true;
        SearchBox.Text = string.Empty;
        _syncingSearch = false;
        SearchBox.PlaceholderText = path == ThisPcPath ? "Buscar en Este equipo" : $"Buscar en {GetTabTitle(path)}";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingSearch || !_explorerProInitialized) return;

        var query = SearchBox.Text.Trim();
        var selected = FilesListView.SelectedItems.OfType<FileEntry>().ToArray();
        Entries.Clear();

        IEnumerable<FileEntry> filtered = _searchMaster;
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(item =>
                item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.TypeLabel.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        foreach (var item in filtered)
            Entries.Add(item);

        RestoreSelection(selected);
        StatusText.Text = string.IsNullOrWhiteSpace(query)
            ? $"{Entries.Count} elemento(s)"
            : $"{Entries.Count} resultado(s) para ‘{query}’";
    }

    private void ViewModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DetailsHeader is null || FilesListView is null || CompactListView is null || IconsGridView is null)
            return;

        var tag = (ViewModeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var mode = tag switch
        {
            "List" => ExplorerViewMode.List,
            "Icons" => ExplorerViewMode.Icons,
            _ => ExplorerViewMode.Details
        };
        ApplyViewMode(mode);
    }

    private void ApplyViewMode(ExplorerViewMode mode)
    {
        _viewMode = mode;
        var selected = FilesListView.SelectedItems.OfType<FileEntry>().ToArray();

        DetailsHeader.Visibility = mode == ExplorerViewMode.Details ? Visibility.Visible : Visibility.Collapsed;
        FilesListView.Visibility = mode == ExplorerViewMode.Details ? Visibility.Visible : Visibility.Collapsed;
        CompactListView.Visibility = mode == ExplorerViewMode.List ? Visibility.Visible : Visibility.Collapsed;
        IconsGridView.Visibility = mode == ExplorerViewMode.Icons ? Visibility.Visible : Visibility.Collapsed;

        RestoreSelection(selected);
    }

    private void RestoreSelection(IEnumerable<FileEntry> selected)
    {
        if (_syncingSelection) return;
        _syncingSelection = true;
        try
        {
            CompactListView.SelectedItems.Clear();
            IconsGridView.SelectedItems.Clear();

            foreach (var item in selected.Where(Entries.Contains))
            {
                if (_viewMode == ExplorerViewMode.List)
                    CompactListView.SelectedItems.Add(item);
                else if (_viewMode == ExplorerViewMode.Icons)
                    IconsGridView.SelectedItems.Add(item);
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void AlternateView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || sender is not ListViewBase source) return;

        _syncingSelection = true;
        try
        {
            FilesListView.SelectedItems.Clear();
            foreach (var item in source.SelectedItems.OfType<FileEntry>())
                FilesListView.SelectedItems.Add(item);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private async void AlternateView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is ListViewBase { SelectedItem: FileEntry item })
            await OpenEntryAsync(item);
    }

    private void AlternateView_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not ListViewBase source) return;

        FrameworkElement? container = FindParent<GridViewItem>(e.OriginalSource as DependencyObject)
            ?? FindParent<ListViewItem>(e.OriginalSource as DependencyObject);

        var item = container switch
        {
            GridViewItem gridItem => gridItem.Content as FileEntry,
            ListViewItem listItem => listItem.Content as FileEntry,
            _ => null
        };
        if (item is null) return;

        _syncingSelection = true;
        try
        {
            source.SelectedItems.Clear();
            source.SelectedItems.Add(item);
            FilesListView.SelectedItems.Clear();
            FilesListView.SelectedItems.Add(item);
        }
        finally
        {
            _syncingSelection = false;
        }

        CreateFileContextMenu(item).ShowAt(container);
        e.Handled = true;
    }

    private void BuildFolderTree()
    {
        FolderTree.RootNodes.Clear();
        _treePaths.Clear();

        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AddTreeRoot("Inicio", user, Symbol.Home, lazy: true);
        AddTreeRoot("Escritorio", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Symbol.GoToStart, lazy: true);
        AddTreeRoot("Documentos", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Symbol.Document, lazy: true);
        AddTreeRoot("Descargas", Path.Combine(user, "Downloads"), Symbol.Download, lazy: true);

        var thisPc = CreateTreeNode("Este equipo", ThisPcPath, Symbol.Folder, lazy: false);
        FolderTree.RootNodes.Add(thisPc);
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var name = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? $"Disco local ({drive.Name.TrimEnd('\\')})"
                : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
            thisPc.Children.Add(CreateTreeNode(name, drive.RootDirectory.FullName, Symbol.Folder, lazy: true));
        }
    }

    private void AddTreeRoot(string name, string path, Symbol symbol, bool lazy)
    {
        if (path != ThisPcPath && !Directory.Exists(path)) return;
        FolderTree.RootNodes.Add(CreateTreeNode(name, path, symbol, lazy));
    }

    private TreeViewNode CreateTreeNode(string name, string path, Symbol symbol, bool lazy)
    {
        var node = new TreeViewNode
        {
            Content = CreateTreeHeader(name, symbol),
            HasUnrealizedChildren = lazy && path != ThisPcPath && Directory.Exists(path)
        };
        _treePaths[node] = path;
        return node;
    }

    private static UIElement CreateTreeHeader(string name, Symbol symbol)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        panel.Children.Add(new SymbolIcon(symbol) { Width = 18, Height = 18 });
        panel.Children.Add(new TextBlock
        {
            Text = name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 170
        });
        return panel;
    }

    private async void FolderTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        var node = args.Node;
        if (!node.HasUnrealizedChildren || !_treePaths.TryGetValue(node, out var path) || !Directory.Exists(path))
            return;

        node.HasUnrealizedChildren = false;
        try
        {
            var directories = await Task.Run(() => Directory.EnumerateDirectories(path)
                .Take(200)
                .Select(p => new DirectoryInfo(p))
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(d => (d.Name, d.FullName))
                .ToList());

            foreach (var directory in directories)
                node.Children.Add(CreateTreeNode(directory.Name, directory.FullName, Symbol.Folder, lazy: true));
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
        catch
        {
        }
    }

    private async void FolderTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        var node = sender.SelectedNode;
        if (node is null || !_treePaths.TryGetValue(node, out var path)) return;
        await NavigateToAsync(path);
    }
}
