using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Cothon.Core.HotKeys;

/// <summary>
/// Registers a global hotkey on OS level via a message-only window on a
/// background thread, so Cothon hears Ctrl+Alt+Space even when unfocused.
/// Callbacks are delivered through the supplied dispatcher (UI thread).
/// </summary>
public sealed class Win32HotkeyHost : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int WmQuit = 0x0012;
    private const int HotkeyId = 0xC074;

    private Thread? _thread;
    private uint _threadId;
    /// <summary>Per-instance class name: two hosts (mic + pill) must not fight over one registration.</summary>
    private readonly string _className = $"CothonHotkeyWindow-{Guid.NewGuid():N}";
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly object _gate = new();

    /// <summary>Non-empty when RegisterHotKey failed (conflict etc.).</summary>
    public string Error { get; private set; } = "";

    public void Start(HotkeyChord chord, Action onTrigger, DispatcherLike dispatcher)
    {
        lock (_gate)
        {
            if (_thread is not null)
                throw new InvalidOperationException("Hotkey host already running.");
        }

        _thread = new Thread(() => Run(chord, onTrigger, dispatcher))
        {
            IsBackground = true,
            Name = "cothon-hotkey",
        };
        _thread.Start();
        if (!_ready.Wait(3000))
            throw new TimeoutException("Hotkey host did not stabilize.");
    }

    private void Run(HotkeyChord chord, Action onTrigger, DispatcherLike dispatcher)
    {
        _threadId = GetCurrentThreadId();

        IntPtr hwnd;
        lock (_gate)
        {
            try
            {
                hwnd = CreateMessageWindow();
            }
            catch (Win32Exception ex)
            {
                Error = $"Hotkey window failed: {ex.Message}";
                _ready.Set();
                return;
            }
        }

        var mods = chord.ModifierFlags();
        if (!RegisterHotKey(hwnd, HotkeyId, mods, chord.VirtualKey))
            Error = $"Hotkey not registered ({chord}) — likely in use elsewhere. Mic toggles via the orb.";
        _ready.Set();

        try
        {
            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WmHotkey)
                    dispatcher.Post(onTrigger);
            }
        }
        finally
        {
            UnregisterHotKey(hwnd, HotkeyId);
            DestroyWindow(hwnd);
        }
    }

    public void Dispose()
    {
        if (_threadId != 0)
            PostThreadMessageW(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        _ready.Dispose();
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static readonly WndProcDelegate WndProcCallback = DefWndProc;

    private static IntPtr DefWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        => DefWindowProcW(hwnd, msg, wParam, lParam);

    private IntPtr CreateMessageWindow()
    {
        var name = _className;
        var handle = GetModuleHandleW(null);
        var cls = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcCallback),
            hInstance = handle,
            lpszClassName = name,
        };
        if (RegisterClassExW(ref cls) == 0)
            throw new Win32Exception("RegisterClassExW failed");
        var hwnd = CreateWindowExW(0, name, name, 0, 0, 0, 0, 0,
            HwndMessageParent, IntPtr.Zero, handle, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
            throw new Win32Exception();
        return hwnd;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    private static readonly IntPtr HwndMessageParent = new(-3);

    [DllImport("user32.dll")]
    private static extern uint RegisterClassExW(ref WndClassEx lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu,
        IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out Msg lpMsg, IntPtr hwnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessageW(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);
}

/// <summary>Dispatcher seam so the Core host stays UI-framework-agnostic and testable.</summary>
public interface DispatcherLike
{
    void Post(Action action);
}
