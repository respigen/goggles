using System.ComponentModel;
using System.Runtime.InteropServices;
namespace Goggles;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        byte alpha = 180;
        var hotkey = Keys.F11;

        // >:(
        if (args.Length > 0)
        {
            if (byte.TryParse(args[0], out var argAlpha))
            {
                alpha = argAlpha;
            }
            if (args.Length > 1)
            {
                if (Enum.TryParse<Keys>(args[1], true, out var argHotkey))
                {
                    hotkey = argHotkey;
                }
            }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.Run(new GogglesApplicationContext(alpha, hotkey));
    }
}

internal sealed class GogglesApplicationContext : ApplicationContext
{
    private const int ToggleHotkeyId = 1;
    private readonly TransparencyManager _transparencyManager;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly NotifyIcon _notifyIcon;

    public const string ApplicationName = "Goggles";

    public GogglesApplicationContext(byte alpha, Keys hotkey)
    {
        _transparencyManager = new TransparencyManager(alpha);
        _hotkeyWindow = new HotkeyWindow();
        _notifyIcon = CreateNotifyIcon();

        try
        {
            _hotkeyWindow.Register(ToggleHotkeyId, HotkeyModifiers.Win | HotkeyModifiers.Control | HotkeyModifiers.NoRepeat, hotkey, OnTransparencyRequested);
        }
        catch (SystemException e)
        {
            ShowErrorAndExit(e.Message);
            return;
        }

        _notifyIcon.ShowBalloonTip(1000, ApplicationName, $"Running. Use Ctrl+Win+{hotkey} for transparency", ToolTipIcon.Info);
    }
    private void OnTransparencyRequested()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd != nint.Zero) _transparencyManager.Toggle(hwnd);
    }

    private NotifyIcon CreateNotifyIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        return new NotifyIcon
        {
            Icon = new Icon("goggles.ico"), // Unsafe, sort of.
            Visible = true,
            Text = ApplicationName,
            ContextMenuStrip = menu
        };
    }

    private void ShowErrorAndExit(string message)
    {
        MessageBox.Show(message, ApplicationName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hotkeyWindow.Dispose();
            _transparencyManager.Dispose();
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}

[Flags]
internal enum HotkeyModifiers : uint
{
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
    NoRepeat = 0x4000
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly Dictionary<int, Action> _callbacks = new();
    private bool _disposed;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "GogglesMessageWindow",
            X = 0,
            Y = 0,
            Height = 0,
            Width = 0,
            Style = unchecked((int)0x80000000),
            ExStyle = 0x00000080,
            Parent = nint.Zero
        });
    }

    public void Register(int id, HotkeyModifiers modifiers, Keys key, Action callback)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(HotkeyWindow));
        }

        if (_callbacks.ContainsKey(id))
        {
            throw new InvalidOperationException($"Hotkey id {id} is already registered.");
        }

        if (!NativeMethods.RegisterHotKey(Handle, id, (uint)modifiers, (uint)key))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterHotKey failed.");
        }

        _callbacks[id] = callback;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && _callbacks.TryGetValue(m.WParam.ToInt32(), out var action))
        {
            action();
            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var kvp in _callbacks)
        {
            NativeMethods.UnregisterHotKey(Handle, kvp.Key);
        }

        _callbacks.Clear();
        DestroyHandle();
        _disposed = true;
    }
}

internal sealed class TransparencyManager : IDisposable
{
    private readonly Dictionary<nint, WindowState> _active = new();
    private readonly byte _alpha;
    private bool _disposed;

    public TransparencyManager(byte alpha)
    {
        _alpha = alpha;
    }

    public void Toggle(nint hwnd)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TransparencyManager));
        }

        if (!NativeMethods.IsWindow(hwnd))
        {
            _active.Remove(hwnd);
            return;
        }

        if (_active.TryGetValue(hwnd, out var state))
        {
            Restore(hwnd, state);
            _active.Remove(hwnd);
            return;
        }

        Apply(hwnd);
    }

    private void Apply(nint hwnd)
    {
        var originalStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        if (originalStyle == nint.Zero && Marshal.GetLastWin32Error() != 0)
        {
            return;
        }

        byte originalAlpha = 255;
        uint originalColorKey = 0;
        bool alphaFlag = false;
        bool colorKeyFlag = false;

        if ((originalStyle.ToInt64() & NativeMethods.WS_EX_LAYERED) != 0 &&
            NativeMethods.GetLayeredWindowAttributes(hwnd, out var colorKey, out var alpha, out var flags))
        {
            originalColorKey = colorKey;
            originalAlpha = alpha;
            alphaFlag = (flags & NativeMethods.LWA_ALPHA) != 0;
            colorKeyFlag = (flags & NativeMethods.LWA_COLORKEY) != 0;
        }

        var newStyle = new nint(originalStyle.ToInt64() | NativeMethods.WS_EX_LAYERED);
        if (NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, newStyle) == nint.Zero && Marshal.GetLastWin32Error() != 0)
        {
            return;
        }

        if (!NativeMethods.SetLayeredWindowAttributes(hwnd, 0, _alpha, NativeMethods.LWA_ALPHA))
        {
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, originalStyle);
            return;
        }

        _active[hwnd] = new WindowState(originalStyle, originalAlpha, originalColorKey, alphaFlag, colorKeyFlag);
    }

    private static void Restore(nint hwnd, WindowState state)
    {
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, state.OriginalStyle);

        uint flags = 0;
        if (state.HasAlpha)
        {
            flags |= NativeMethods.LWA_ALPHA;
        }

        if (state.HasColorKey)
        {
            flags |= NativeMethods.LWA_COLORKEY;
        }

        if (flags != 0)
        {
            NativeMethods.SetLayeredWindowAttributes(hwnd, state.OriginalColorKey, state.OriginalAlpha, flags);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var entry in _active)
        {
            if (NativeMethods.IsWindow(entry.Key))
            {
                Restore(entry.Key, entry.Value);
            }
        }

        _active.Clear();
        _disposed = true;
    }

    private readonly struct WindowState
    {
        public WindowState(nint originalStyle, byte originalAlpha, uint originalColorKey, bool hasAlpha, bool hasColorKey)
        {
            OriginalStyle = originalStyle;
            OriginalAlpha = originalAlpha;
            OriginalColorKey = originalColorKey;
            HasAlpha = hasAlpha;
            HasColorKey = hasColorKey;
        }

        public nint OriginalStyle { get; }
        public byte OriginalAlpha { get; }
        public uint OriginalColorKey { get; }
        public bool HasAlpha { get; }
        public bool HasColorKey { get; }
    }
}
internal static class NativeMethods
{
    public const int WM_HOTKEY = 0x0312;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_LAYERED = 0x80000;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const uint LWA_COLORKEY = 0x1;
    public const uint LWA_ALPHA = 0x2;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public static readonly nint HWND_TOPMOST = new(-1);
    public static readonly nint HWND_NOTOPMOST = new(-2);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetLayeredWindowAttributes(nint hWnd, out uint pcrKey, out byte pbAlpha, out uint pdwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetLayeredWindowAttributes(nint hWnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
