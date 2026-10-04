using System.Runtime.InteropServices;

namespace ImpulsaExplorer.Services;

internal static class NativeShellContextMenuService
{
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint CMF_EXPLORE = 0x00000004;
    private const uint CMF_CANRENAME = 0x00000010;
    private const uint CMF_EXTENDEDVERBS = 0x00000100;
    private const uint CMIC_MASK_UNICODE = 0x00004000;
    private const uint CMIC_MASK_ASYNCOK = 0x00100000;
    private const uint CMIC_MASK_PTINVOKE = 0x20000000;
    private const int SW_SHOWNORMAL = 1;
    private const int VK_SHIFT = 0x10;

    private const uint WM_DRAWITEM = 0x002B;
    private const uint WM_MEASUREITEM = 0x002C;
    private const uint WM_INITMENUPOPUP = 0x0117;
    private const uint WM_MENUCHAR = 0x0120;

    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");
    private static readonly SubclassProcDelegate SubclassProc = ShellSubclassProc;

    private static IContextMenu2? _activeContextMenu2;
    private static IContextMenu3? _activeContextMenu3;

    public static bool Show(IEnumerable<string> selectedPaths)
    {
        var paths = selectedPaths
            .Where(p => !string.IsNullOrWhiteSpace(p) && (File.Exists(p) || Directory.Exists(p)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (paths.Length == 0)
            return false;

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            hwnd = GetActiveWindow();
        if (hwnd == IntPtr.Zero)
            return false;

        var absolutePidls = new List<IntPtr>(paths.Length);
        IntPtr pidlArray = IntPtr.Zero;
        IntPtr parentPtr = IntPtr.Zero;
        IntPtr contextMenuPtr = IntPtr.Zero;
        IntPtr menu = IntPtr.Zero;
        object? parentObject = null;
        object? contextMenuObject = null;

        try
        {
            foreach (var path in paths)
            {
                var hr = SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _);
                if (hr < 0 || pidl == IntPtr.Zero)
                    return false;
                absolutePidls.Add(pidl);
            }

            var iidShellFolder = IID_IShellFolder;
            var bindHr = SHBindToParent(absolutePidls[0], ref iidShellFolder, out parentPtr, out _);
            if (bindHr < 0 || parentPtr == IntPtr.Zero)
                return false;

            parentObject = Marshal.GetObjectForIUnknown(parentPtr);
            if (parentObject is not IShellFolder parentFolder)
                return false;

            pidlArray = Marshal.AllocCoTaskMem(IntPtr.Size * absolutePidls.Count);
            for (var i = 0; i < absolutePidls.Count; i++)
            {
                var childPidl = ILFindLastID(absolutePidls[i]);
                if (childPidl == IntPtr.Zero)
                    return false;
                Marshal.WriteIntPtr(pidlArray, i * IntPtr.Size, childPidl);
            }

            var iidContextMenu = IID_IContextMenu;
            var getMenuHr = parentFolder.GetUIObjectOf(
                hwnd,
                (uint)absolutePidls.Count,
                pidlArray,
                ref iidContextMenu,
                IntPtr.Zero,
                out contextMenuPtr);

            if (getMenuHr < 0 || contextMenuPtr == IntPtr.Zero)
                return false;

            contextMenuObject = Marshal.GetObjectForIUnknown(contextMenuPtr);
            if (contextMenuObject is not IContextMenu contextMenu)
                return false;

            menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
                return false;

            var flags = CMF_EXPLORE | CMF_CANRENAME;
            if ((GetKeyState(VK_SHIFT) & 0x8000) != 0)
                flags |= CMF_EXTENDEDVERBS;

            var queryHr = contextMenu.QueryContextMenu(menu, 0, 1, 0x7FFF, flags);
            if (queryHr < 0)
                return false;

            _activeContextMenu3 = contextMenuObject as IContextMenu3;
            _activeContextMenu2 = _activeContextMenu3 ?? contextMenuObject as IContextMenu2;

            var subclassInstalled = SetWindowSubclass(hwnd, SubclassProc, new UIntPtr(0x494D5055), UIntPtr.Zero);
            try
            {
                if (!GetCursorPos(out var point))
                    point = new POINT { X = 20, Y = 20 };

                var command = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, point.X, point.Y, hwnd, IntPtr.Zero);
                if (command == 0)
                    return true;

                var verbOffset = command - 1;
                var invokeInfo = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                    fMask = CMIC_MASK_UNICODE | CMIC_MASK_ASYNCOK | CMIC_MASK_PTINVOKE,
                    hwnd = hwnd,
                    lpVerb = new IntPtr(verbOffset),
                    lpVerbW = new IntPtr(verbOffset),
                    nShow = SW_SHOWNORMAL,
                    ptInvoke = point
                };

                var invokeHr = contextMenu.InvokeCommand(ref invokeInfo);
                return invokeHr >= 0;
            }
            finally
            {
                if (subclassInstalled)
                    RemoveWindowSubclass(hwnd, SubclassProc, new UIntPtr(0x494D5055));
                _activeContextMenu3 = null;
                _activeContextMenu2 = null;
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            if (menu != IntPtr.Zero)
                DestroyMenu(menu);
            if (contextMenuPtr != IntPtr.Zero)
                Marshal.Release(contextMenuPtr);
            if (parentPtr != IntPtr.Zero)
                Marshal.Release(parentPtr);
            if (pidlArray != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pidlArray);

            foreach (var pidl in absolutePidls)
            {
                if (pidl != IntPtr.Zero)
                    CoTaskMemFree(pidl);
            }

            if (contextMenuObject is not null && Marshal.IsComObject(contextMenuObject))
            {
                try { Marshal.FinalReleaseComObject(contextMenuObject); } catch { }
            }
            if (parentObject is not null && Marshal.IsComObject(parentObject))
            {
                try { Marshal.FinalReleaseComObject(parentObject); } catch { }
            }
        }
    }

    private static IntPtr ShellSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (_activeContextMenu3 is not null &&
            (msg == WM_INITMENUPOPUP || msg == WM_DRAWITEM || msg == WM_MEASUREITEM || msg == WM_MENUCHAR))
        {
            try
            {
                var hr = _activeContextMenu3.HandleMenuMsg2(msg, wParam, lParam, out var result);
                if (hr >= 0)
                    return result;
            }
            catch { }
        }
        else if (_activeContextMenu2 is not null &&
                 (msg == WM_INITMENUPOPUP || msg == WM_DRAWITEM || msg == WM_MEASUREITEM))
        {
            try
            {
                var hr = _activeContextMenu2.HandleMenuMsg(msg, wParam, lParam);
                if (hr >= 0)
                    return IntPtr.Zero;
            }
            catch { }
        }

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public POINT ptInvoke;
    }

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, IntPtr pszDisplayName, ref uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, IntPtr apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, IntPtr pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    [ComImport]
    [Guid("000214F4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2 : IContextMenu
    {
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport]
    [Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3 : IContextMenu2
    {
        [PreserveSig] int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
    }

    private delegate IntPtr SubclassProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);

    [DllImport("shell32.dll")]
    private static extern IntPtr ILFindLastID(IntPtr pidl);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProcDelegate callback, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProcDelegate callback, UIntPtr id);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
