using Avalonia.Controls;
using Avalonia.Input;
using Cothon.Ui.ViewModels;

namespace Cothon;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainWindowViewModel();
        DataContext = _vm;
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
