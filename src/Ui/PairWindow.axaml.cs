using Avalonia.Controls;
using Avalonia.Interactivity;
using Cothon.Ui.ViewModels;

namespace Cothon;

public partial class PairWindow : Window
{
    public PairWindow()
    {
        InitializeComponent();
    }

    public PairWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>Production opener: the dialog lives until the user closes it.</summary>
    public static Action Open(SettingsViewModel viewModel) => () =>
    {
        var window = new PairWindow(viewModel);
        window.Show();
    };

    public void OnClose(object? sender, RoutedEventArgs args)
    {
        Close();
    }
}
