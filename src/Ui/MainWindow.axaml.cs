using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Cothon.Core.HotKeys;
using Cothon.Ui.ViewModels;

namespace Cothon;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private Win32HotkeyHost? _hotkeyHost;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainWindowViewModel();
        DataContext = _vm;
        Loaded += OnLoaded;
        Closed += OnClosed;
        _vm.Timeline.CollectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(() => DrawerScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    /// <summary>Register Ctrl+Alt+Space OS-wide; degrade gracefully if in use.</summary>
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_hotkeyHost is not null)
            return;

        var host = new Win32HotkeyHost();
        _hotkeyHost = host;
        try
        {
            host.Start(
                HotkeyChord.Parse("Ctrl+Alt+Space"),
                () => Dispatcher.UIThread.Post(_vm.ToggleMic),
                new AvaloniaDispatcherBridge());
        }
        catch (Exception ex)
        {
            _vm.NoteSystem($"Mic hotkey failed: {ex.Message} — tap the orb to arm the mic.");
            return;
        }

        if (host.Error.Length > 0)
            _vm.NoteSystem(host.Error);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _hotkeyHost?.Dispose();
        _hotkeyHost = null;
    }

    private sealed class AvaloniaDispatcherBridge : DispatcherLike
    {
        public void Post(Action action)
            => Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            e.Handled = true;
            _vm.SendCommand.Execute(null);
        }
    }

    /// <summary>Orb tap mirrors the hotkey: one mic switch, every surface agrees.</summary>
    private void OnOrbTapped(object? sender, TappedEventArgs e)
        => _vm.ToggleMic();
}
