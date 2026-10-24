using Avalonia.Controls;
using Avalonia.Input;
using Cothon.Ui.ViewModels;

namespace Cothon;

/// <summary>
/// The voice-first compact surface: mini orb, always on top, click to
/// summon the main window, drag anywhere to park it next to your work.
/// </summary>
public partial class PillWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly MainWindow _owner;

    public PillWindow()
    {
        InitializeComponent();
    }

    public PillWindow(MainWindow owner, MainWindowViewModel vm)
        : this()
    {
        _owner = owner;
        _vm = vm;
        DataContext = _vm;
    }

    private void OnDragGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnPillTapped(object? sender, TappedEventArgs e)
    {
        _owner.WindowState = WindowState.Normal;
        _owner.Show();
        _owner.Activate();
    }
}
