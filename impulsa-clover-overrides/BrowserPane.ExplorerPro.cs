using System.Diagnostics;
using ImpulsaExplorer.Models;
using ImpulsaExplorer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

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

        // RightTapped was unreliable on some desktop mouse setups. Use the
        // native context request event so right-click, Shift+F10 and the menu
        // key all open the same menu.
        FilesListView.RightTapped -= FilesListView_RightTapped;
        CompactListView.RightTapped -= AlternateView_RightTapped;
        IconsGridView.RightTapped -= AlternateView_RightTapped;
        FilesListView.ContextRequested += FileArea_ContextRequested;
        CompactListView.ContextRequested += FileArea_ContextRequested;
        IconsGridView.ContextRequested += FileArea_ContextRequested;

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

        FrameworkElement? container = (FrameworkElement?)FindParent<GridViewItem>(e.OriginalSource as DependencyObject)
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

    private void FileArea_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (sender is not ListViewBase source) return;

        var origin = e.OriginalSource as DependencyObject;
        FrameworkElement? container = FindParent<ListViewItem>(origin);
        container ??= FindParent<GridViewItem>(origin);

        var item = container switch
        {
            ListViewItem listItem => listItem.Content as FileEntry,
            GridViewItem gridItem => gridItem.Content as FileEntry,
            _ => null
        };

        if (item is null && source.SelectedItem is FileEntry selected)
        {
            item = selected;
            container = source.ContainerFromItem(selected) as FrameworkElement;
        }

        if (item is null || container is null)
        {
            CreateFolderBackgroundMenu().ShowAt(source);
            e.Handled = true;
            return;
        }

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

    private MenuFlyout CreateFolderBackgroundMenu()
    {
        var menu = new MenuFlyout();

        var paste = new MenuFlyoutItem
        {
            Text = "Pegar",
            Icon = new SymbolIcon(Symbol.Paste),
            IsEnabled = Directory.Exists(CurrentPath) && FileOperationService.Current.HasClipboard
        };
        paste.Click += async (_, _) =>
        {
            if (!Directory.Exists(CurrentPath) || !FileOperationService.Current.HasClipboard) return;
            try
            {
                SetBusy(true, "Pegando…");
                await FileOperationService.Current.PasteAsync(CurrentPath);
                await NavigateToAsync(CurrentPath, false);
            }
            catch (Exception ex)
            {
                SetBusy(false, ex.Message);
            }
        };
        menu.Items.Add(paste);

        var refresh = new MenuFlyoutItem
        {
            Text = "Actualizar",
            Icon = new SymbolIcon(Symbol.Sync)
        };
        refresh.Click += async (_, _) => await NavigateToAsync(CurrentPath, false);
        menu.Items.Add(refresh);

        if (Directory.Exists(CurrentPath))
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            var properties = new MenuFlyoutItem { Text = "Propiedades" };
            properties.Click += (_, _) => ShowProperties(CurrentPath);
            menu.Items.Add(properties);
        }

        return menu;
    }

    private void BuildFolderTree()
    {
        FolderTree.RootNodes.Clear();
        _treePaths.Clear();

        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var downloads = Path.Combine(user, "Downloads");
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        var quick = CreateGroupNode("Acceso rápido", Symbol.OutlineStar);
        var seenQuick = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddChildIfDirectory(quick, "Escritorio", desktop, Symbol.GoToStart, seenQuick);
        AddChildIfDirectory(quick, "Descargas", downloads, Symbol.Download, seenQuick);
        AddChildIfDirectory(quick, "Documentos", documents, Symbol.Document, seenQuick);
        AddChildIfDirectory(quick, "Imágenes", pictures, Symbol.Pictures, seenQuick);

        foreach (var favorite in StateService.Current.State.Favorites.Where(Directory.Exists))
        {
            if (!seenQuick.Add(favorite)) continue;
            var name = Path.GetFileName(favorite.TrimEnd('\\'));
            if (string.IsNullOrWhiteSpace(name)) name = favorite;
            quick.Children.Add(CreateTreeNode(name, favorite, Symbol.OutlineStar, lazy: true));
        }

        var oneDrives = new[]
        {
            Environment.GetEnvironmentVariable("OneDrive"),
            Environment.GetEnvironmentVariable("OneDriveConsumer"),
            Environment.GetEnvironmentVariable("OneDriveCommercial")
        }
        .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Cast<string>()
        .ToArray();

        foreach (var oneDrive in oneDrives)
        {
            var name = oneDrives.Length == 1 ? "OneDrive" : $"OneDrive - {Path.GetFileName(oneDrive.TrimEnd('\\'))}";
            var node = CreateTreeNode(name, oneDrive, Symbol.Folder, lazy: true);
            node.IsExpanded = false;
            FolderTree.RootNodes.Add(node);
        }

        var thisPc = CreateTreeNode("Este equipo", ThisPcPath, Symbol.Folder, lazy: false);
        thisPc.IsExpanded = true;
        FolderTree.RootNodes.Add(thisPc);

        AddChildIfDirectory(thisPc, "Descargas", downloads, Symbol.Download);
        AddChildIfDirectory(thisPc, "Documentos", documents, Symbol.Document);
        AddChildIfDirectory(thisPc, "Escritorio", desktop, Symbol.GoToStart);
        AddChildIfDirectory(thisPc, "Imágenes", pictures, Symbol.Pictures);
        AddChildIfDirectory(thisPc, "Música", music, Symbol.MusicInfo);
        AddChildIfDirectory(thisPc, "Vídeos", videos, Symbol.Video);

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var name = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? $"Disco local ({drive.Name.TrimEnd('\\')})"
                : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
            thisPc.Children.Add(CreateTreeNode(name, drive.RootDirectory.FullName, Symbol.Folder, lazy: true));
        }

        FolderTree.RootNodes.Add(CreateTreeNode("Papelera de reciclaje", "shell:RecycleBinFolder", Symbol.Delete, lazy: false));
        FolderTree.RootNodes.Add(CreateTreeNode("Red", "shell:NetworkPlacesFolder", Symbol.Folder, lazy: false));
    }

    private TreeViewNode CreateGroupNode(string name, Symbol symbol)
    {
        var node = new TreeViewNode
        {
            Content = new TreeLocation { Name = name, Path = string.Empty, Symbol = symbol },
            IsExpanded = true
        };
        FolderTree.RootNodes.Add(node);
        return node;
    }

    private void AddChildIfDirectory(TreeViewNode parent, string name, string path, Symbol symbol, HashSet<string>? seen = null)
    {
        if (!Directory.Exists(path)) return;
        if (seen is not null && !seen.Add(path)) return;
        parent.Children.Add(CreateTreeNode(name, path, symbol, lazy: true));
    }

    private TreeViewNode CreateTreeNode(string name, string path, Symbol symbol, bool lazy)
    {
        var item = new TreeLocation
        {
            Name = name,
            Path = path,
            Symbol = symbol
        };

        var node = new TreeViewNode
        {
            Content = item,
            HasUnrealizedChildren = lazy && !path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && path != ThisPcPath && Directory.Exists(path)
        };
        _treePaths[node] = path;
        _ = LoadTreeIconAsync(item);
        return node;
    }

    private static async Task LoadTreeIconAsync(TreeLocation item)
    {
        if (string.IsNullOrWhiteSpace(item.Path) || item.Path == ThisPcPath || item.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(item.Path)) return;
        try
        {
            var root = Path.GetPathRoot(item.Path);
            var isDrive = !string.IsNullOrWhiteSpace(root) &&
                          string.Equals(root.TrimEnd('\\'), item.Path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            var icon = await ShellIconService.GetIconAsync(item.Path, true, isDrive);
            if (icon is not null)
                item.IconSource = icon;
        }
        catch
        {
        }
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
                .Take(250)
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
        if (node is null) return;

        var path = (node.Content as TreeLocation)?.Path;
        if (string.IsNullOrWhiteSpace(path) && !_treePaths.TryGetValue(node, out path)) return;
        if (string.IsNullOrWhiteSpace(path)) return;

        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusText.Text = $"No se pudo abrir: {ex.Message}";
            }
            return;
        }

        await NavigateToAsync(path);
    }

    private void FavoriteAndRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        FavoriteButton_Click(sender, e);
        BuildFolderTree();
    }

    private void FileArea_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var paths = e.Items.OfType<FileEntry>()
            .Select(i => i.FullPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (paths.Length == 0) return;
        e.Data.SetText(string.Join(Environment.NewLine, paths));
        e.Data.RequestedOperation = DataPackageOperation.Copy;
    }

    private void FileArea_DragOver(object sender, DragEventArgs e)
    {
        if (CurrentPath == ThisPcPath || !Directory.Exists(CurrentPath)) return;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems) && !e.DataView.Contains(StandardDataFormats.Text)) return;

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Copiar a esta carpeta";
        e.DragUIOverride.IsCaptionVisible = true;
        e.Handled = true;
    }

    private async void FileArea_Drop(object sender, DragEventArgs e)
    {
        if (CurrentPath == ThisPcPath || !Directory.Exists(CurrentPath)) return;

        var paths = new List<string>();
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                paths.AddRange(items.Select(i => i.Path).Where(p => !string.IsNullOrWhiteSpace(p)));
            }
            else if (e.DataView.Contains(StandardDataFormats.Text))
            {
                var text = await e.DataView.GetTextAsync();
                paths.AddRange(text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            paths = paths
                .Where(p => File.Exists(p) || Directory.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (paths.Count == 0) return;

            SetBusy(true, "Copiando archivos arrastrados…");
            FileOperationService.Current.SetClipboard(paths, false);
            await FileOperationService.Current.PasteAsync(CurrentPath);
            await NavigateToAsync(CurrentPath, false);
        }
        catch (Exception ex)
        {
            SetBusy(false, $"No se pudo completar el arrastre: {ex.Message}");
        }
    }
}
