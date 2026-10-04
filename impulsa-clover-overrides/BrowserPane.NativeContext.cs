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

        FilesListView.ContextRequested += FileArea_NativeContextRequested;
        CompactListView.ContextRequested += FileArea_NativeContextRequested;
        IconsGridView.ContextRequested += FileArea_NativeContextRequested;

        InstallMoreViews();
    }

    private async void FileArea_NativeContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not ListViewBase source)
            return;

        FrameworkElement? container = (FrameworkElement?)FindParent<GridViewItem>(args.OriginalSource as DependencyObject)
            ?? FindParent<ListViewItem>(args.OriginalSource as DependencyObject);

        if (container is null)
        {
            ShowBackgroundContextMenu(source);
            args.Handled = true;
            return;
        }

        var item = container switch
        {
            GridViewItem gridItem => gridItem.Content as FileEntry,
            ListViewItem listItem => listItem.Content as FileEntry,
            _ => null
        };

        if (item is null)
            return;

        if (!source.SelectedItems.Contains(item))
        {
            source.SelectedItems.Clear();
            source.SelectedItems.Add(item);
        }

        SyncSelectionToDetails(source);

        var paths = source.SelectedItems
            .OfType<FileEntry>()
            .Select(entry => entry.FullPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (paths.Length == 0)
            paths = [item.FullPath];

        args.Handled = true;

        var nativeMenuSucceeded = await LaunchIsolatedShellMenuAsync(paths);
        if (!nativeMenuSucceeded)
        {
            // A third-party Shell extension may have crashed the helper process.
            // Keep the main explorer alive and fall back to our safe menu.
            CreateFileContextMenu(item).ShowAt(container);
            StatusText.Text = "Una extensión del menú de Windows falló; se mostró el menú seguro de Impulsa.";
            return;
        }

        if (CurrentPath != ThisPcPath && Directory.Exists(CurrentPath))
            await NavigateToAsync(CurrentPath, false);
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
            var properties = new MenuFlyoutItem { Text = "Propiedades" };
            properties.Click += (_, _) => ShowProperties(CurrentPath);
            menu.Items.Add(properties);
        }

        menu.ShowAt(target);
    }
}
