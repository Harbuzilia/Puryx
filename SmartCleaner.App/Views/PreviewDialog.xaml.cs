using System.Windows;

namespace SmartCleaner.App.Views;

public partial class PreviewDialog : Window
{
    public bool Confirmed { get; private set; }

    public PreviewDialog()
    {
        InitializeComponent();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }
}
