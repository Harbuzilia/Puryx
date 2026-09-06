using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Knowledge;
using SmartCleaner.Core.Models;
using System.Collections.ObjectModel;
using System.IO;

namespace SmartCleaner.App.ViewModels;

public partial class DiscoveryViewModel : ObservableObject
{
    private readonly IKnowledgeBase _knowledgeBase;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "Выберите папку для сканирования";

    [ObservableProperty]
    private string _scanPath = "";

    public ObservableCollection<DiscoveredAppViewModel> DiscoveredApps { get; } = new();

    public DiscoveryViewModel(IKnowledgeBase knowledgeBase)
    {
        _knowledgeBase = knowledgeBase;
        
        // Дефолтные пути для сканирования
        ScanPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(ScanPath))
            return;

        IsScanning = true;
        StatusText = $"Сканирование {ScanPath}...";
        DiscoveredApps.Clear();

        try
        {
            var rootPath = Path.GetFullPath(ScanPath);
            if (!Directory.Exists(rootPath))
            {
                StatusText = "Папка не существует";
                return;
            }

            await Task.Run(() =>
            {
                var discovered = _knowledgeBase.DiscoverUnknownApps(rootPath);
                foreach (var app in discovered)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        DiscoveredApps.Add(new DiscoveredAppViewModel(app, _knowledgeBase));
                    });
                }
            });

            StatusText = $"Найдено {DiscoveredApps.Count} неизвестных приложений";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void AddAllSafe()
    {
        foreach (var app in DiscoveredApps.Where(a => !a.IsAdded && a.HasCacheSuggestions))
        {
            app.AddToKnowledgeBaseCommand.Execute(null);
        }
    }
}

public partial class DiscoveredAppViewModel : ObservableObject
{
    private readonly IKnowledgeBase _knowledgeBase;
    public DiscoveredApp App { get; }

    public string Path => App.Path;
    public string Name => App.SuggestedName;
    public string SizeFormatted => FormatSize(App.TotalSize);
    
    public IReadOnlyList<string> CacheFolders => App.SuggestedCacheFolders;
    public IReadOnlyList<string> ProtectedFiles => App.SuggestedProtectedFiles;
    
    public bool HasCacheSuggestions => CacheFolders.Count > 0;
    public bool HasProtectedSuggestions => ProtectedFiles.Count > 0;

    public string CacheFoldersText => string.Join(", ", CacheFolders);
    public string ProtectedFilesText => string.Join(", ", ProtectedFiles);

    [ObservableProperty]
    private bool _isAdded;

    public DiscoveredAppViewModel(DiscoveredApp app, IKnowledgeBase knowledgeBase)
    {
        App = app;
        _knowledgeBase = knowledgeBase;
    }

    [RelayCommand]
    private void AddToKnowledgeBase()
    {
        if (IsAdded) return;

        var appDef = new AppDefinition
        {
            Id = App.SuggestedName.ToLowerInvariant().Replace(" ", "_"),
            DisplayName = App.SuggestedName,
            RootPaths = [App.Path],
            CachePatterns = App.SuggestedCacheFolders.Select(f => f + "/**").ToList(),
            ProtectedPatterns = App.SuggestedProtectedFiles.ToList(),
            IsUserDefined = true
        };

        _knowledgeBase.AddUserApp(appDef);
        IsAdded = true;
    }

    private static string FormatSize(long bytes) => SizeFormatter.Format(bytes);
}
