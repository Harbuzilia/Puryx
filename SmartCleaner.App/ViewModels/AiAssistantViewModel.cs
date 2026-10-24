using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.Core.AiAssistant;
using SmartCleaner.Core.Localization;
using System.Collections.ObjectModel;
using System.Windows;

namespace SmartCleaner.App.ViewModels;

public partial class AiAssistantViewModel : ObservableObject
{
    private readonly NaturalLanguageQueryEngine _engine;

    [ObservableProperty]
    private string _userInput = string.Empty;

    [ObservableProperty]
    private bool _isThinking;

    public ObservableCollection<AiChatMessage> Messages { get; } = new();

    public event Action<string>? NavigationRequested;

    public AiAssistantViewModel(NaturalLanguageQueryEngine engine)
    {
        _engine = engine;

        // Welcome greeting message
        Messages.Add(new AiChatMessage
        {
            IsUser = false,
            Text = Localizer.GetString("AiAssistant_Msg_Welcome")
        });
    }

    [RelayCommand]
    public async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput) || IsThinking) return;

        var query = UserInput.Trim();
        UserInput = string.Empty;

        // Add user message
        Messages.Add(new AiChatMessage
        {
            IsUser = true,
            Text = query
        });

        IsThinking = true;

        try
        {
            await Task.Delay(400); // realistic typing feedback
            var reply = await _engine.ProcessUserQueryAsync(query);
            Messages.Add(reply);
        }
        catch (Exception ex)
        {
            Messages.Add(new AiChatMessage
            {
                IsUser = false,
                Text = Localizer.GetString("AiAssistant_Msg_ProcessError", ex.Message)
            });
        }
        finally
        {
            IsThinking = false;
        }
    }

    [RelayCommand]
    public void ExecuteAction(AiChatMessage? msg)
    {
        if (msg == null || string.IsNullOrWhiteSpace(msg.ActionPayload)) return;

        NavigationRequested?.Invoke(msg.ActionPayload);
    }
}
