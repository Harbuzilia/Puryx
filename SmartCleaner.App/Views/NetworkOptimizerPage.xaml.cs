using SmartCleaner.App.ViewModels;
using System.Windows.Controls;

namespace SmartCleaner.App.Views;

public partial class NetworkOptimizerPage : UserControl
{
    public NetworkOptimizerPage(NetworkOptimizerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
