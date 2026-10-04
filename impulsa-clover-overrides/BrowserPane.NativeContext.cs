using System.Diagnostics;
using ImpulsaExplorer.Models;
using ImpulsaExplorer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ImpulsaExplorer.Controls;

public sealed partial class BrowserPane
{
    private bool _nativeContextMenusInstalled;

    private void InstallNativeContextMenus()
    {
        if (_nativeContextMenusInstalled)
            return;

        _nativeContextMenusInstalled = true;

        FilesListView.RightTapped -= FilesListView_RightTapped;
        CompactListView.RightTapped -= AlternateView_RightTapped;
        IconsGridView.RightTapped -= AlternateView_RightTapped;

        FilesListView.ContextRequested -= FileArea_ContextRequested;
        CompactListView.ContextRequested -= FileArea_ContextRequested;
        IconsGridView.ContextRequested -= FileArea_ContextRequested;

        FilesListView.ContextRequested += FileArea_NativeContextRequested;
        CompactListView.ContextRequested += FileArea_NativeContextRequested;
        IconsGridView.ContextRequested += FileArea_NativeContextRequested;

        InstallMoreViews();
    }

    private void FileArea_NativeContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not ListViewBase source)
            return;

        var origin = args.OriginalSource as DependencyObject;
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
            ShowBackgroundContextMenu(source);
            args.Handled = true;
            return;
        }

        if (!source.SelectedItems.Contains(item))
        {
            source.SelectedItems.Clear();
            source.SelectedItems.Add(item);
        }

        SyncSelectionToDetails(source);
        args.Handled = true;

        BuildSafeFileContextMenu(source, item).ShowAt(container);
    }

    private MenuFlyout BuildSafeFileContextMenu(ListViewBase source, FileEntry item)
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
        properties.Click += (_, _) =>
        {
            if (!ShellPropertiesService.Show(item.FullPath))
                StatusText.Text = "Windows no pudo abrir las propiedades de este elemento.";
        };
        menu.Items.Add(properties);

        menu.Items.Add(new MenuFlyoutSeparator());

        var moreWindows = new MenuFlyoutItem { Text = "Más opciones de Windows…" };
        moreWindows.Click += async (_, _) =>
        {
            var paths = source.SelectedItems
                .OfType<FileEntry>()
                .Select(entry => entry.FullPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (paths.Length == 0)
                paths = [item.FullPath];

            StatusText.Text = "Abriendo opciones de Windows…";
            var ok = await LaunchIsolatedShellMenuAsync(paths);
            if (!ok)
            {
                StatusText.Text = "El menú extendido de Windows no respondió. El menú seguro sigue disponible.";
                return;
            }

            if (CurrentPath != ThisPcPath && Directory.Exists(CurrentPath))
                await NavigateToAsync(CurrentPath, false);
        };
        menu.Items.Add(moreWindows);

        return menu;
    }

    private async Task<bool> LaunchIsolatedShellMenuAsync(IReadOnlyCollection<string> paths)
    {
        string? requestFile = null;
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
                return false;

            var requestDirectory = Path.Combine(Path.GetTempPath(), "ImpulsaExplorer", "ShellMenu");
            Directory.CreateDirectory(requestDirectory);
            requestFile = Path.Combine(requestDirectory, $"menu-{Guid.NewGuid():N}.txt");
            await File.WriteAllLinesAsync(requestFile, paths);

            var startInfo = new ProcessStartInfo(processPath)
            {
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(ShellMenuHostRunner.RequestArgument);
            startInfo.ArgumentList.Add(requestFile);

            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(requestFile) && File.Exists(requestFile))
                    File.Delete(requestFile);
            }
            catch
            {
            }
        }
    }

    private void SyncSelectionToDetails(ListViewBase source)
    {
        if (ReferenceEquals(source, FilesListView))
            return;

        _syncingSelection = true;
        try
        {
            FilesListView.SelectedItems.Clear();
            foreach (var selected in source.SelectedItems.OfType<FileEntry>())
                FilesListView.SelectedItems.Add(selected);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void ShowBackgroundContextMenu(FrameworkElement target)
    {
        var menu = new MenuFlyout();

        var paste = new MenuFlyoutItem
        {
            Text = "Pegar",
            Icon = new SymbolIcon(Symbol.Paste),
            IsEnabled = Directory.Exists(CurrentPath) && FileOperationService.Current.HasClipboard
        };
        paste.Click += PasteButton_Click;
        menu.Items.Add(paste);

        var refresh = new MenuFlyoutItem
        {
            Text = "Actualizar",
            Icon = new SymbolIcon(Symbol.Sync)
        };
        refresh.Click += RefreshButton_Click;
        menu.Items.Add(refresh);

        menu.Items.Add(new MenuFlyoutSeparator());

        var newFolder = new MenuFlyoutItem
        {
            Text = "Nueva carpeta",
            Icon = new SymbolIcon(Symbol.Add),
            IsEnabled = Directory.Exists(CurrentPath)
        };
        newFolder.Click += NewFolderButton_Click;
        menu.Items.Add(newFolder);

        if (Directory.Exists(CurrentPath))
        {
            var properties = new MenuFlyoutItem { Text = "Propiedades de esta carpeta" };
            properties.Click += (_, _) =>
            {
                if (!ShellPropertiesService.Show(CurrentPath))
                    StatusText.Text = "Windows no pudo abrir las propiedades de esta carpeta.";
            };
            menu.Items.Add(properties);
        }

        menu.ShowAt(target);
    }
}
