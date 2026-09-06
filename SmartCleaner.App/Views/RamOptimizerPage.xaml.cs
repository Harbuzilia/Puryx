using SmartCleaner.App.ViewModels;
using System.Windows.Controls;

namespace SmartCleaner.App.Views;

public partial class RamOptimizerPage : UserControl
{
    public RamOptimizerPage(RamOptimizerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
