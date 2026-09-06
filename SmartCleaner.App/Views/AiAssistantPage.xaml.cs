using System.Windows.Controls;
using System.Windows.Input;

namespace SmartCleaner.App.Views;

public partial class AiAssistantPage : UserControl
{
    public AiAssistantPage()
    {
        InitializeComponent();
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            if (DataContext is ViewModels.AiAssistantViewModel vm && vm.SendMessageCommand.CanExecute(null))
            {
                vm.SendMessageCommand.Execute(null);
            }
        }
    }
}
