using System.Collections.ObjectModel;
using System.Diagnostics;
using ImpulsaExplorer.Models;
using ImpulsaExplorer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace ImpulsaExplorer.Controls;

public sealed partial class BrowserPane : UserControl
{
    public const string ThisPcPath = "impulsa://thispc";

    public ObservableCollection<FileEntry> Entries { get; } = [];
    public ObservableCollection<BreadcrumbItem> Breadcrumbs { get; } = [];
    public string CurrentPath { get; private set; } = ThisPcPath;

    public event EventHandler<string>? LocationChanged;
    public event EventHandler<string>? TitleChanged;
    public event EventHandler<string>? OpenInNewTabRequested;

    private readonly List<string> _history = [];
    private int _historyIndex = -1;
    private CancellationTokenSource? _loadCts;

    public BrowserPane()
    {
        InitializeComponent();
        BuildQuickAccess();
    }

    public void FocusAddressBar()
    {
        AddressBox.Text = CurrentPath == ThisPcPath ? "Este equipo" : CurrentPath;
        PathBreadcrumbBar.Visibility = Visibility.Collapsed;
        AddressBox.Visibility = Visibility.Visible;
        AddressBox.Focus(FocusState.Programmatic);
        AddressBox.SelectAll();
    }

    public async Task InitializeAsync(string? initialPath = null)
    {
        await NavigateToAsync(NormalizeInitialPath(initialPath), true);
    }

    public async Task NavigateToAsync(string path, bool addHistory = true)
    {
        path = string.IsNullOrWhiteSpace(path) ? ThisPcPath : path.Trim();
        if (path != ThisPcPath && !Directory.Exists(path))
        {
            StatusText.Text = "La ruta no existe o no está disponible.";
            return;
        }

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        SetBusy(true, "Cargando…");

        try
        {
            var results = path == ThisPcPath
                ? await Task.Run(LoadDrives, token)
                : await Task.Run(() => LoadDirectory(path, token), token);

            token.ThrowIfCancellationRequested();
            Entries.Clear();
            foreach (var item in results) Entries.Add(item);

            CurrentPath = path;
            AddressBox.Text = path == ThisPcPath ? "Este equipo" : path;
            UpdateBreadcrumbs(path);
            ShowBreadcrumbs();

            if (addHistory)
            {
                if (_historyIndex < _history.Count - 1)
                    _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
                _history.Add(path);
                _historyIndex = _history.Count - 1;
            }

            UpdateHistoryButtons();
            SetBusy(false, $"{Entries.Count} elemento(s)");
            LocationChanged?.Invoke(this, path);
            TitleChanged?.Invoke(this, GetTabTitle(path));
            _ = LoadWindowsIconsAsync(results, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (UnauthorizedAccessException)
        {
            SetBusy(false, "Acceso denegado.");
        }
        catch (Exception ex)
        {
            SetBusy(false, $"No se pudo abrir: {ex.Message}");
        }
    }

    private static List<FileEntry> LoadDrives()
    {
        return DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => new FileEntry
            {
                Name = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Name : $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})",
                FullPath = d.RootDirectory.FullName,
                IsFolder = true,
                IsDrive = true,
                TypeLabel = $"Unidad {d.DriveType}",
                FallbackSymbol = Symbol.Folder
            })
            .ToList();
    }

    private static List<FileEntry> LoadDirectory(string path, CancellationToken token)
    {
        var output = new List<FileEntry>();
        var directory = new DirectoryInfo(path);

        foreach (var dir in directory.EnumerateDirectories())
        {
            token.ThrowIfCancellationRequested();
            try
            {
                output.Add(new FileEntry
                {
                    Name = dir.Name,
                    FullPath = dir.FullName,
                    IsFolder = true,
                    Modified = dir.LastWriteTime,
                    TypeLabel = "Carpeta",
                    FallbackSymbol = Symbol.Folder
                });
            }
            catch { }
        }

        foreach (var file in directory.EnumerateFiles())
        {
            token.ThrowIfCancellationRequested();
            try
            {
                output.Add(new FileEntry
                {
                    Name = file.Name,
                    FullPath = file.FullName,
                    SizeBytes = file.Length,
                    Modified = file.LastWriteTime,
                    TypeLabel = string.IsNullOrWhiteSpace(file.Extension) ? "Archivo" : file.Extension.TrimStart('.').ToUpperInvariant(),
                    FallbackSymbol = GetFileSymbol(file.Extension)
                });
            }
            catch { }
        }

        return output
            .OrderByDescending(x => x.IsFolder)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static Symbol GetFileSymbol(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" => Symbol.Pictures,
        ".mp4" or ".mkv" or ".avi" or ".mov" => Symbol.Video,
        ".mp3" or ".wav" or ".flac" or ".ogg" => Symbol.Audio,
        _ => Symbol.Document
    };

    private async Task LoadWindowsIconsAsync(IReadOnlyList<FileEntry> items, CancellationToken token)
    {
        try
        {
            foreach (var batch in items.Chunk(12))
            {
                token.ThrowIfCancellationRequested();
                await Task.WhenAll(batch.Select(item => LoadWindowsIconAsync(item, token)));
                await Task.Yield();
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private static async Task LoadWindowsIconAsync(FileEntry item, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        var icon = await ShellIconService.GetIconAsync(item.FullPath, item.IsFolder, item.IsDrive);
        if (!token.IsCancellationRequested && icon is not null)
            item.IconSource = icon;
    }

    private void BuildQuickAccess()
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var items = new List<QuickLocation>
        {
            new() { Name = "Inicio", Path = user, Symbol = Symbol.Home },
            new() { Name = "Escritorio", Path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Symbol = Symbol.GoToStart },
            new() { Name = "Descargas", Path = Path.Combine(user, "Downloads"), Symbol = Symbol.Download },
            new() { Name = "Documentos", Path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Symbol = Symbol.Document },
            new() { Name = "Imágenes", Path = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Symbol = Symbol.Pictures },
            new() { Name = "Música", Path = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), Symbol = Symbol.MusicInfo },
            new() { Name = "Videos", Path = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), Symbol = Symbol.Video },
            new() { Name = "Este equipo", Path = ThisPcPath, Symbol = Symbol.Folder }
        };

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? $"Disco local ({drive.Name.TrimEnd('\\')})"
                : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
            items.Add(new QuickLocation { Name = label, Path = drive.RootDirectory.FullName, Symbol = Symbol.Folder });
        }

        foreach (var favorite in StateService.Current.State.Favorites.Where(Directory.Exists))
        {
            if (items.All(i => !string.Equals(i.Path, favorite, StringComparison.OrdinalIgnoreCase)))
            {
                var name = Path.GetFileName(favorite.TrimEnd('\\')) is { Length: > 0 } n ? n : favorite;
                items.Add(new QuickLocation { Name = name, Path = favorite, Symbol = Symbol.OutlineStar });
            }
        }

        QuickAccessList.ItemsSource = items;
        _ = LoadQuickAccessIconsAsync(items);
    }

    private static async Task LoadQuickAccessIconsAsync(IEnumerable<QuickLocation> items)
    {
        foreach (var item in items)
        {
            if (item.Path == ThisPcPath || !Directory.Exists(item.Path)) continue;
            try
            {
                var root = Path.GetPathRoot(item.Path);
                var isDrive = !string.IsNullOrWhiteSpace(root) && string.Equals(root.TrimEnd('\\'), item.Path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                var icon = await ShellIconService.GetIconAsync(item.Path, true, isDrive);
                if (icon is not null) item.IconSource = icon;
            }
            catch { }
        }
    }

    private void UpdateBreadcrumbs(string path)
    {
        Breadcrumbs.Clear();
        Breadcrumbs.Add(new BreadcrumbItem { Name = "Este equipo", Path = ThisPcPath });
        if (path == ThisPcPath) return;

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root)) return;

        var rootName = root.TrimEnd('\\');
        Breadcrumbs.Add(new BreadcrumbItem { Name = string.IsNullOrWhiteSpace(rootName) ? root : rootName, Path = root });

        var relative = path[root.Length..].Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(relative)) return;

        var current = root;
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            Breadcrumbs.Add(new BreadcrumbItem { Name = segment, Path = current });
        }
    }

    private static string NormalizeInitialPath(string? initialPath)
    {
        if (initialPath == ThisPcPath) return ThisPcPath;
        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath)) return initialPath;
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string GetTabTitle(string path)
    {
        if (path == ThisPcPath) return "Este equipo";
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar);
        var title = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(title) ? path : title;
    }

    private void ShowBreadcrumbs()
    {
        AddressBox.Visibility = Visibility.Collapsed;
        PathBreadcrumbBar.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy, string status)
    {
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = status;
    }

    private void UpdateHistoryButtons()
    {
        BackButton.IsEnabled = _historyIndex > 0;
        ForwardButton.IsEnabled = _historyIndex >= 0 && _historyIndex < _history.Count - 1;
        UpButton.IsEnabled = CurrentPath != ThisPcPath && Directory.GetParent(CurrentPath) is not null;
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_historyIndex <= 0) return;
        _historyIndex--;
        await NavigateToAsync(_history[_historyIndex], false);
    }

    private async void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_historyIndex >= _history.Count - 1) return;
        _historyIndex++;
        await NavigateToAsync(_history[_historyIndex], false);
    }

    private async void UpButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPath == ThisPcPath) return;
        var parent = Directory.GetParent(CurrentPath);
        if (parent is not null) await NavigateToAsync(parent.FullName);
        else await NavigateToAsync(ThisPcPath);
    }

    private async void HomeButton_Click(object sender, RoutedEventArgs e) =>
        await NavigateToAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await NavigateToAsync(CurrentPath, false);

    private void AddressEditButton_Click(object sender, RoutedEventArgs e) => FocusAddressBar();
    private void AddressBox_LostFocus(object sender, RoutedEventArgs e) => ShowBreadcrumbs();

    private async void AddressBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            AddressBox.Text = CurrentPath == ThisPcPath ? "Este equipo" : CurrentPath;
            ShowBreadcrumbs();
            return;
        }

        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        var typed = AddressBox.Text.Trim();
        if (string.Equals(typed, "Este equipo", StringComparison.OrdinalIgnoreCase)) typed = ThisPcPath;
        await NavigateToAsync(Environment.ExpandEnvironmentVariables(typed));
        ShowBreadcrumbs();
    }

    private async void PathBreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is BreadcrumbItem crumb)
            await NavigateToAsync(crumb.Path);
    }

    private async void QuickAccessList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is QuickLocation location)
            await NavigateToAsync(location.Path);
    }

    private async void FilesListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FilesListView.SelectedItem is FileEntry item)
            await OpenEntryAsync(item);
    }

    private async Task OpenEntryAsync(FileEntry item)
    {
        if (item.IsFolder || item.IsDrive)
        {
            await NavigateToAsync(item.FullPath);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"No se pudo abrir: {ex.Message}";
        }
    }

    private void FilesListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var container = FindParent<ListViewItem>(e.OriginalSource as DependencyObject);
        if (container?.Content is not FileEntry item) return;

        if (!container.IsSelected)
        {
            FilesListView.SelectedItems.Clear();
            container.IsSelected = true;
        }

        CreateFileContextMenu(item).ShowAt(container);
        e.Handled = true;
    }

    private MenuFlyout CreateFileContextMenu(FileEntry item)
    {
        var menu = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = "Abrir", Icon = new SymbolIcon(Symbol.OpenFile) };
        open.Click += async (_, _) => await OpenEntryAsync(item);
        menu.Items.Add(open);

        if (item.IsFolder || item.IsDrive)
        {
            var newTab = new MenuFlyoutItem { Text = "Abrir en nueva pestaña", Icon = new SymbolIcon(Symbol.Add) };
            newTab.Click += (_, _) => OpenInNewTabRequested?.Invoke(this, item.FullPath);
            menu.Items.Add(newTab);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        var cut = new MenuFlyoutItem { Text = "Cortar", Icon = new SymbolIcon(Symbol.Cut) };
        cut.Click += (_, _) => CutSelected();
        menu.Items.Add(cut);
        var copy = new MenuFlyoutItem { Text = "Copiar", Icon = new SymbolIcon(Symbol.Copy) };
        copy.Click += (_, _) => CopySelected();
        menu.Items.Add(copy);
        var copyPath = new MenuFlyoutItem { Text = "Copiar ruta" };
        copyPath.Click += (_, _) => CopyPath(item.FullPath);
        menu.Items.Add(copyPath);

        menu.Items.Add(new MenuFlyoutSeparator());
        var rename = new MenuFlyoutItem { Text = "Cambiar nombre", Icon = new SymbolIcon(Symbol.Rename) };
        rename.Click += async (_, _) => await RenameSelectedAsync();
        menu.Items.Add(rename);
        var delete = new MenuFlyoutItem { Text = "Eliminar", Icon = new SymbolIcon(Symbol.Delete) };
        delete.Click += async (_, _) => await DeleteSelectedAsync();
        menu.Items.Add(delete);

        menu.Items.Add(new MenuFlyoutSeparator());
        var properties = new MenuFlyoutItem { Text = "Propiedades" };
        properties.Click += (_, _) => ShowProperties(item.FullPath);
        menu.Items.Add(properties);
        return menu;
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match) return match;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void FilesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var count = FilesListView.SelectedItems.Count;
        StatusText.Text = count == 0 ? $"{Entries.Count} elemento(s)" : $"{count} seleccionado(s)";
    }

    private IEnumerable<string> SelectedPaths() => FilesListView.SelectedItems.OfType<FileEntry>().Select(i => i.FullPath);

    private async void NewFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(CurrentPath)) return;
        var nameBox = new TextBox { PlaceholderText = "Nombre de la carpeta", Text = "Nueva carpeta", SelectionStart = 0, SelectionLength = 13 };
        var dialog = CreateDialog("Nueva carpeta", nameBox, "Crear");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var name = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            Directory.CreateDirectory(FileOperationService.MakeUniquePath(Path.Combine(CurrentPath, name)));
            await NavigateToAsync(CurrentPath, false);
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void CopySelected()
    {
        var paths = SelectedPaths().ToArray();
        if (paths.Length == 0) return;
        FileOperationService.Current.SetClipboard(paths, false);
        StatusText.Text = $"{paths.Length} elemento(s) preparados para copiar.";
    }

    private void CutSelected()
    {
        var paths = SelectedPaths().ToArray();
        if (paths.Length == 0) return;
        FileOperationService.Current.SetClipboard(paths, true);
        StatusText.Text = $"{paths.Length} elemento(s) preparados para mover.";
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e) => CopySelected();
    private void CutButton_Click(object sender, RoutedEventArgs e) => CutSelected();

    private async void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(CurrentPath) || !FileOperationService.Current.HasClipboard) return;
        SetBusy(true, "Pegando…");
        try
        {
            await FileOperationService.Current.PasteAsync(CurrentPath);
            await NavigateToAsync(CurrentPath, false);
        }
        catch (Exception ex) { SetBusy(false, ex.Message); }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e) => await DeleteSelectedAsync();

    private async Task DeleteSelectedAsync()
    {
        var paths = SelectedPaths().ToArray();
        if (paths.Length == 0) return;
        var dialog = CreateDialog("Enviar a la Papelera", new TextBlock { Text = $"Se enviarán {paths.Length} elemento(s) a la Papelera de reciclaje.", TextWrapping = TextWrapping.Wrap }, "Continuar");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            SetBusy(true, "Enviando a la Papelera…");
            await FileOperationService.Current.DeleteToRecycleBinAsync(paths);
            await NavigateToAsync(CurrentPath, false);
        }
        catch (Exception ex) { SetBusy(false, ex.Message); }
    }

    private async void RenameButton_Click(object sender, RoutedEventArgs e) => await RenameSelectedAsync();

    private async Task RenameSelectedAsync()
    {
        if (FilesListView.SelectedItems.Count != 1 || FilesListView.SelectedItem is not FileEntry item) return;
        var nameBox = new TextBox { Text = item.Name, SelectionStart = 0, SelectionLength = item.Name.Length };
        var dialog = CreateDialog("Cambiar nombre", nameBox, "Guardar");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var newName = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;
        var parent = Path.GetDirectoryName(item.FullPath);
        if (string.IsNullOrWhiteSpace(parent)) return;
        var destination = Path.Combine(parent, newName);
        try
        {
            if (item.IsFolder) Directory.Move(item.FullPath, destination);
            else File.Move(item.FullPath, destination);
            await NavigateToAsync(CurrentPath, false);
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(CurrentPath)) return;
        var favorites = StateService.Current.State.Favorites;
        var existing = favorites.FirstOrDefault(f => string.Equals(f, CurrentPath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            favorites.Add(CurrentPath);
            StatusText.Text = "Agregado a Favoritos.";
        }
        else
        {
            favorites.Remove(existing);
            StatusText.Text = "Quitado de Favoritos.";
        }
        StateService.Current.Save();
        BuildQuickAccess();
    }

    private static void CopyPath(string path)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(path);
            Clipboard.SetContent(package);
        }
        catch { }
    }

    private void ShowProperties(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "properties" });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"No se pudieron abrir las propiedades: {ex.Message}";
        }
    }

    private ContentDialog CreateDialog(string title, object content, string primaryText) => new()
    {
        Title = title,
        Content = content,
        PrimaryButtonText = primaryText,
        CloseButtonText = "Cancelar",
        DefaultButton = ContentDialogButton.Primary,
        XamlRoot = XamlRoot
    };
}
