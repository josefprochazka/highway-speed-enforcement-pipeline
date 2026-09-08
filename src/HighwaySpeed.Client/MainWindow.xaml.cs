using System.Windows;

namespace HighwaySpeed.Client;

/// <summary>
/// Code-behind for the dashboard window. Intentionally empty apart from
/// <see cref="InitializeComponent"/> - all behaviour lives in the view-model.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
