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
    private Win32HotkeyHost? _pillHotkeyHost;
    private PillWindow? _pill;
    private bool _pillVisible = true;

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

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(new SettingsViewModel());
        settings.Show();
    }

    /// <summary>Register the settings-chosen hotkeys OS-wide; degrade gracefully if in use.</summary>
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_hotkeyHost is not null)
            return;

        var stored = Core.Settings.AppSettings.Load();

        // Always-on-top pill shares the same VM; both surfaces never disagree.
        if (_pill is null)
        {
            _pill = new PillWindow(this, _vm);
            _pill.Show();
        }

        var chordText = string.IsNullOrWhiteSpace(stored.Hotkey) ? "Ctrl+Alt+Space" : stored.Hotkey;

        var host = new Win32HotkeyHost();
        _hotkeyHost = host;
        try
        {
            host.Start(
                HotkeyChord.Parse(chordText),
                () => Dispatcher.UIThread.Post(_vm.ToggleMic),
                new AvaloniaDispatcherBridge());
        }
        catch (Exception ex)
        {
            _vm.NoteSystem($"Mic hotkey failed: {ex.Message} — tap the orb to arm the mic.");
        }

        if (host.Error.Length > 0)
            _vm.NoteSystem(host.Error);

        RegisterPillHotkey(stored);
    }

    /// <summary>A second, independent chord: hide or reveal the floating pill on demand.</summary>
    private void RegisterPillHotkey(Core.Settings.AppSettings stored)
    {
        var chordText = stored.PillHotkey.Trim();
        if (chordText.Length == 0)
        {
            _vm.NoteSystem("Pill hotkey is off — the floating orb stays visible.");
            return;
        }

        var pillHost = new Win32HotkeyHost();
        _pillHotkeyHost = pillHost;
        try
        {
            pillHost.Start(
                HotkeyChord.Parse(chordText),
                () => Dispatcher.UIThread.Post(TogglePill),
                new AvaloniaDispatcherBridge());
        }
        catch (Exception ex)
        {
            _vm.NoteSystem($"Pill hotkey failed: {ex.Message}");
        }

        if (pillHost.Error.Length > 0)
            _vm.NoteSystem(pillHost.Error);
    }

    private void TogglePill()
    {
        if (_pill is null)
            return;
        if (_pillVisible)
            _pill.Hide();
        else
            _pill.Show();
        _pillVisible = !_pillVisible;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _hotkeyHost?.Dispose();
        _hotkeyHost = null;
        _pillHotkeyHost?.Dispose();
        _pillHotkeyHost = null;
        _pill?.Close();
        _pill = null;
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
