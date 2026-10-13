using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using NotchHub.App.Interop;
using NotchHub.Core;
using NotchHub.UI;
using NotchHub.UI.Localization;

namespace NotchHub.App.Shell;

/// <summary>Notification-area icon (own Shell_NotifyIcon wrapper) with Open panel / Settings / Exit menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = NativeMethods.WM_APP + 1;
    private static readonly uint TaskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
    private readonly HwndSource _source;
    private readonly IntPtr _icon;
    private bool _added;
    private ContextMenu? _menu;

    public event EventHandler? OpenRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public TrayIcon()
    {
        _source = new HwndSource(new HwndSourceParameters("NotchHubTray")
        {
            WindowStyle = NativeMethods.WS_POPUP,
            ExtendedWindowStyle = (int)NativeMethods.WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0,
        });
        _source.AddHook(WndProc);
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        _icon = NativeMethods.LoadImage(IntPtr.Zero, path, NativeMethods.IMAGE_ICON,
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON), NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSMICON),
            NativeMethods.LR_LOADFROMFILE);
        if (_icon == IntPtr.Zero) Log.Warn($"Tray icon not loaded from {path}");
        Add();
    }

    private NativeMethods.NOTIFYICONDATAW Data() => new()
    {
        cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
        hWnd = _source.Handle,
        uID = 1,
        uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = "NotchHub",
        szInfo = "",
        szInfoTitle = "",
    };

    private void Add()
    {
        var data = Data();
        _added = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
        if (!_added) { Log.Warn("Shell_NotifyIcon(NIM_ADD) failed"); return; }
        data.uVersion = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref data);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            switch (lParam.ToInt32() & 0xFFFF)
            {
                case NativeMethods.NIN_SELECT:
                case NativeMethods.NIN_KEYSELECT:
                    OpenRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case NativeMethods.WM_CONTEXTMENU:
                    ShowMenu();
                    break;
            }
            handled = true;
        }
        else if (msg == TaskbarCreated)
        {
            Add(); // Explorer restarted
        }
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        _menu ??= CreateMenu();
        var items = _menu.Items.OfType<MenuItem>().ToArray();
        items[0].Header = Loc.Get("shell.tray.open");
        items[1].Header = Loc.Get("shell.tray.settings");
        items[2].Header = Loc.Get("shell.tray.exit");
        _menu.IsOpen = true;
        if (PresentationSource.FromVisual(_menu) is HwndSource src) NativeMethods.SetForegroundWindow(src.Handle);
    }

    private ContextMenu CreateMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint, FontFamily = NhFonts.Sans, FontSize = 13 };
        menu.Items.Add(Item((_, _) => OpenRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(Item((_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item((_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));
        return menu;

        static MenuItem Item(RoutedEventHandler click)
        {
            var item = new MenuItem { Padding = new Thickness(6, 4, 6, 4) };
            item.Click += click;
            return item;
        }
    }

    /// <summary>Removes the icon (safe from any thread; idempotent).</summary>
    public void Remove()
    {
        if (!_added) return;
        _added = false;
        var data = Data();
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
    }

    public void Dispose()
    {
        Remove();
        if (_icon != IntPtr.Zero) NativeMethods.DestroyIcon(_icon);
        if (_source.Dispatcher.CheckAccess()) _source.Dispose();
    }
}
