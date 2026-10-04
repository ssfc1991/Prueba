using ImpulsaExplorer.Controls;
using ImpulsaExplorer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace ImpulsaExplorer;

public sealed partial class MainWindow : Window
{
    private readonly Stack<string> _closedTabs = new();
    private TabView MainTabs = null!;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Impulsa Explorer — Clover Edition";
        CreateTabView();
        ConfigureTabViewSafely();

        try
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1320, 840));
        }
        catch { }

        TryEnableMica();
        Closed += MainWindow_Closed;
        ConfigureKeyboardShortcuts();
        _ = RestoreSessionAsync();
    }

    private void CreateTabView()
    {
        MainTabs = new TabView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        MainTabs.AddTabButtonClick += MainTabs_AddTabButtonClick;
        MainTabs.TabCloseRequested += MainTabs_TabCloseRequested;
        MainTabs.SelectionChanged += MainTabs_SelectionChanged;
        RootGrid.Children.Add(MainTabs);
    }

    private void ConfigureTabViewSafely()
    {
        try
        {
            MainTabs.CanDragTabs = true;
            MainTabs.CanReorderTabs = true;
            MainTabs.TabWidthMode = TabViewWidthMode.SizeToContent;
            MainTabs.IsAddTabButtonVisible = true;
            MainTabs.Margin = new Thickness(0);
        }
        catch { }
    }

    private async Task RestoreSessionAsync()
    {
        try
        {
            var tabs = StateService.Current.State.SessionTabs
                .Where(p => p == BrowserPane.ThisPcPath || Directory.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();

            if (tabs.Count == 0)
                tabs.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

            foreach (var path in tabs)
                await AddTabAsync(path, select: false);

            if (MainTabs.TabItems.Count > 0)
                MainTabs.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            App.WriteLog("Fallo restaurando sesión", ex);
            if (MainTabs.TabItems.Count == 0)
            {
                try { await AddTabAsync(BrowserPane.ThisPcPath, true); }
                catch (Exception fallbackEx) { App.WriteLog("Fallo creando pestaña de recuperación", fallbackEx); }
            }
        }
    }

    private async Task<TabViewItem> AddTabAsync(string? path = null, bool select = true)
    {
        var pane = new BrowserPane();
        var tab = new TabViewItem
        {
            Header = "Nueva pestaña",
            IconSource = new SymbolIconSource { Symbol = Symbol.Folder },
            Content = pane,
            IsClosable = true
        };

        tab.ContextFlyout = CreateTabFlyout(tab);
        pane.TitleChanged += (_, title) => tab.Header = title;
        pane.OpenInNewTabRequested += (_, requestedPath) => _ = AddTabAsync(requestedPath);
        pane.LocationChanged += async (_, newPath) =>
        {
            ToolTipService.SetToolTip(tab, newPath == BrowserPane.ThisPcPath ? "Este equipo" : newPath);
            await UpdateTabIconAsync(tab, newPath);
        };

        MainTabs.TabItems.Add(tab);
        await pane.InitializeAsync(path);

        if (select) MainTabs.SelectedItem = tab;
        return tab;
    }

    private static async Task UpdateTabIconAsync(TabViewItem tab, string path)
    {
        try
        {
            if (path == BrowserPane.ThisPcPath)
            {
                tab.IconSource = new SymbolIconSource { Symbol = Symbol.Folder };
                return;
            }

            var icon = await ShellIconService.GetIconAsync(path, true, false);
            tab.IconSource = icon is null
                ? new SymbolIconSource { Symbol = Symbol.Folder }
                : new ImageIconSource { ImageSource = icon };
        }
        catch
        {
            tab.IconSource = new SymbolIconSource { Symbol = Symbol.Folder };
        }
    }

    private async void MainTabs_AddTabButtonClick(TabView sender, object args)
    {
        try
        {
            await AddTabAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        catch (Exception ex)
        {
            App.WriteLog("Fallo creando nueva pestaña", ex);
        }
    }

    private void MainTabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        CloseTab(args.Tab, remember: true);
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainTabs.SelectedItem is TabViewItem { Content: BrowserPane pane })
            Title = $"{(pane.CurrentPath == BrowserPane.ThisPcPath ? "Este equipo" : pane.CurrentPath)} — Impulsa Explorer";
    }

    private void TryEnableMica()
    {
        try { SystemBackdrop = new MicaBackdrop(); }
        catch { }
    }

    private MenuFlyout CreateTabFlyout(TabViewItem tab)
    {
        var menu = new MenuFlyout();

        var newTab = new MenuFlyoutItem { Text = "Nueva pestaña", Icon = new SymbolIcon(Symbol.Add) };
        newTab.Click += (_, _) => _ = AddTabAsync();
        menu.Items.Add(newTab);

        var duplicate = new MenuFlyoutItem { Text = "Duplicar pestaña", Icon = new SymbolIcon(Symbol.Copy) };
        duplicate.Click += (_, _) =>
        {
            if (tab.Content is BrowserPane pane) _ = AddTabAsync(pane.CurrentPath);
        };
        menu.Items.Add(duplicate);

        var reopen = new MenuFlyoutItem { Text = "Reabrir pestaña cerrada" };
        reopen.Click += (_, _) => RestoreClosedTab();
        menu.Items.Add(reopen);

        menu.Items.Add(new MenuFlyoutSeparator());

        var close = new MenuFlyoutItem { Text = "Cerrar pestaña", Icon = new SymbolIcon(Symbol.Cancel) };
        close.Click += (_, _) => CloseTab(tab, remember: true);
        menu.Items.Add(close);

        var closeOthers = new MenuFlyoutItem { Text = "Cerrar otras pestañas" };
        closeOthers.Click += (_, _) => CloseOtherTabs(tab);
        menu.Items.Add(closeOthers);

        var closeRight = new MenuFlyoutItem { Text = "Cerrar pestañas a la derecha" };
        closeRight.Click += (_, _) => CloseTabsToRight(tab);
        menu.Items.Add(closeRight);

        return menu;
    }

    private void CloseOtherTabs(TabViewItem keep)
    {
        var tabs = MainTabs.TabItems.OfType<TabViewItem>().Where(t => !ReferenceEquals(t, keep)).ToArray();
        foreach (var tab in tabs) CloseTab(tab, remember: true, ensureOneTab: false);
        MainTabs.SelectedItem = keep;
    }

    private void CloseTabsToRight(TabViewItem anchor)
    {
        var tabs = MainTabs.TabItems.OfType<TabViewItem>().ToList();
        var index = tabs.IndexOf(anchor);
        if (index < 0) return;
        foreach (var tab in tabs.Skip(index + 1).ToArray())
            CloseTab(tab, remember: true, ensureOneTab: false);
        MainTabs.SelectedItem = anchor;
    }

    private void CloseTab(TabViewItem tab, bool remember, bool ensureOneTab = true)
    {
        if (remember && tab.Content is BrowserPane pane)
            _closedTabs.Push(pane.CurrentPath);

        MainTabs.TabItems.Remove(tab);
        if (ensureOneTab && MainTabs.TabItems.Count == 0)
            _ = AddTabAsync();
    }

    private void SelectNextTab()
    {
        if (MainTabs.TabItems.Count < 2) return;
        MainTabs.SelectedIndex = (MainTabs.SelectedIndex + 1) % MainTabs.TabItems.Count;
    }

    private void SelectPreviousTab()
    {
        if (MainTabs.TabItems.Count < 2) return;
        var next = MainTabs.SelectedIndex - 1;
        MainTabs.SelectedIndex = next < 0 ? MainTabs.TabItems.Count - 1 : next;
    }

    private void ConfigureKeyboardShortcuts()
    {
        AddShortcut(VirtualKey.T, VirtualKeyModifiers.Control, async () => await AddTabAsync());
        AddShortcut(VirtualKey.W, VirtualKeyModifiers.Control, CloseCurrentTab);
        AddShortcut(VirtualKey.T, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, RestoreClosedTab);
        AddShortcut(VirtualKey.L, VirtualKeyModifiers.Control, FocusAddressBar);
        AddShortcut(VirtualKey.Tab, VirtualKeyModifiers.Control, SelectNextTab);
        AddShortcut(VirtualKey.Tab, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, SelectPreviousTab);
    }

    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, e) =>
        {
            action();
            e.Handled = true;
        };
        RootGrid.KeyboardAccelerators.Add(accelerator);
    }

    private void CloseCurrentTab()
    {
        if (MainTabs.SelectedItem is TabViewItem tab)
            CloseTab(tab, remember: true);
    }

    private void RestoreClosedTab()
    {
        if (_closedTabs.Count > 0)
            _ = AddTabAsync(_closedTabs.Pop());
    }

    private void FocusAddressBar()
    {
        if (MainTabs.SelectedItem is TabViewItem { Content: BrowserPane pane })
            pane.FocusAddressBar();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        StateService.Current.State.SessionTabs = MainTabs.TabItems
            .OfType<TabViewItem>()
            .Select(t => t.Content)
            .OfType<BrowserPane>()
            .Select(p => p.CurrentPath)
            .ToList();
        StateService.Current.Save();
    }
}
