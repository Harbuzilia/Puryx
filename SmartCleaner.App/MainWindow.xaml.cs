using Microsoft.Extensions.DependencyInjection;
using SmartCleaner.App.ViewModels;
using SmartCleaner.App.Views;
using SmartCleaner.Core.Models;
using System.Windows;
using System.Windows.Controls;

namespace SmartCleaner.App;

public partial class MainWindow : Window
{
    private SettingsView? _settingsView;
    private DiscoveryView? _discoveryView;
    private DuplicatesPage? _duplicatesPage;
    private StartupPage? _startupPage;
    private DiskMapPage? _diskMapPage;
    private LargeFilesPage? _largeFilesPage;
    private SchedulerPage? _schedulerPage;
    private DashboardPage? _dashboardPage;
    private CliInspectorPage? _cliInspectorPage;
    private AiAssistantPage? _aiAssistantPage;
    private UninstallerPage? _uninstallerPage;
    private CompactPage? _compactPage;
    private SystemDeepCleanPage? _systemDeepCleanPage;
    private QuarantinePage? _quarantinePage;
    private PluginsPage? _pluginsPage;
    private RamOptimizerPage? _ramOptimizerPage;
    private NetworkOptimizerPage? _networkOptimizerPage;
    private PrivacyDebloatPage? _privacyDebloatPage;
    private ServicesOptimizerPage? _servicesOptimizerPage;

    public MainWindow()
    {
        InitializeComponent();
        AllowDrop = true;
        Drop += MainWindow_Drop;
        
        // Ctrl+F → фокус на поле поиска
        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.FocusSearchRequested += (_, _) => SearchBox.Focus();
            }
        };
    }

    /// <summary>
    /// Обработка Drag & Drop — сканировать перетащенные папки
    /// </summary>
    private async void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && DataContext is MainViewModel vm)
        {
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            await vm.ScanDroppedPathsAsync(paths);
        }
    }

    /// <summary>
    /// Показать MainView с фильтром "Все категории"
    /// </summary>
    private void NavigateToAllCategories(object sender, RoutedEventArgs e)
    {
        ShowMainView();
        if (DataContext is MainViewModel vm)
        {
            vm.SelectCategoryCommand.Execute(null);
        }
    }

    /// <summary>
    /// Показать MainView с фильтром по конкретной категории (через dynamic sidebar)
    /// </summary>
    private void NavigateToCategory(object sender, RoutedEventArgs e)
    {
        ShowMainView();

        if (sender is RadioButton rb && rb.DataContext is CategorySummary cat && DataContext is MainViewModel vm)
        {
            vm.SelectCategoryCommand.Execute(cat.Name);
        }
    }

    private void NavigateToSettings(object sender, RoutedEventArgs e)
    {
        if (_settingsView == null && App.Services != null)
        {
            _settingsView = new SettingsView
            {
                DataContext = App.Services.GetRequiredService<SettingsViewModel>()
            };
        }
        
        ContentArea.Content = _settingsView;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void NavigateToDiscovery(object sender, RoutedEventArgs e)
    {
        if (_discoveryView == null && App.Services != null)
        {
            _discoveryView = new DiscoveryView
            {
                DataContext = App.Services.GetRequiredService<DiscoveryViewModel>()
            };
        }
        
        ContentArea.Content = _discoveryView;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void NavigateToDuplicates(object sender, RoutedEventArgs e)
    {
        if (_duplicatesPage == null && App.Services != null)
        {
            _duplicatesPage = new DuplicatesPage
            {
                DataContext = App.Services.GetRequiredService<DuplicatesViewModel>()
            };
        }

        ContentArea.Content = _duplicatesPage;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void NavigateToStartup(object sender, RoutedEventArgs e)
    {
        if (_startupPage == null && App.Services != null)
        {
            _startupPage = new StartupPage
            {
                DataContext = App.Services.GetRequiredService<StartupViewModel>()
            };
        }

        ContentArea.Content = _startupPage;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void NavigateToDiskMap(object sender, RoutedEventArgs e)
    {
        if (_diskMapPage == null && App.Services != null)
        {
            _diskMapPage = new DiskMapPage
            {
                DataContext = App.Services.GetRequiredService<DiskMapViewModel>()
            };
        }

        ContentArea.Content = _diskMapPage;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void NavigateToLargeFiles(object sender, RoutedEventArgs e)
    {
        if (_largeFilesPage == null && App.Services != null)
        {
            _largeFilesPage = new LargeFilesPage
            {
                DataContext = App.Services.GetRequiredService<LargeFilesViewModel>()
            };
        }
        ContentArea.Content = _largeFilesPage;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void NavigateToScheduler(object sender, RoutedEventArgs e)
    {
        if (_schedulerPage == null && App.Services != null)
        {
            _schedulerPage = new SchedulerPage
            {
                DataContext = App.Services.GetRequiredService<SchedulerViewModel>()
            };
        }
        ContentArea.Content = _schedulerPage;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void NavigateToDashboard(object sender, RoutedEventArgs e)
    {
        if (_dashboardPage == null && App.Services != null)
        {
            _dashboardPage = new DashboardPage
            {
                DataContext = App.Services.GetRequiredService<DashboardViewModel>()
            };
        }
        ContentArea.Content = _dashboardPage;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void NavigateToCliInspector(object sender, RoutedEventArgs e)
    {
        if (_cliInspectorPage == null && App.Services != null)
        {
            _cliInspectorPage = new CliInspectorPage
            {
                DataContext = App.Services.GetRequiredService<CliInspectorViewModel>()
            };
        }
        ContentArea.Content = _cliInspectorPage;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void NavigateToAiAssistant(object sender, RoutedEventArgs e)
    {
        if (_aiAssistantPage == null && App.Services != null)
        {
            var vm = App.Services.GetRequiredService<AiAssistantViewModel>();
            vm.NavigationRequested += payload =>
            {
                switch (payload)
                {
                    case "Duplicates": NavigateToDuplicates(this, new RoutedEventArgs()); break;
                    case "LargeFiles": NavigateToLargeFiles(this, new RoutedEventArgs()); break;
                    case "Compact": NavigateToCompact(this, new RoutedEventArgs()); break;
                    case "Uninstaller": NavigateToUninstaller(this, new RoutedEventArgs()); break;
                    case "DeepClean": NavigateToSystemDeepClean(this, new RoutedEventArgs()); break;
                    default: ShowMainView(); break;
                }
            };
            _aiAssistantPage = new AiAssistantPage { DataContext = vm };
        }
        ContentArea.Content = _aiAssistantPage;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void NavigateToUninstaller(object sender, RoutedEventArgs e)
    {
        if (_uninstallerPage == null && App.Services != null)
        {
            var vm = App.Services.GetRequiredService<UninstallerViewModel>();
            _uninstallerPage = new UninstallerPage { DataContext = vm };
            _ = vm.LoadAppsAsync();
        }
        ContentArea.Content = _uninstallerPage;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void NavigateToCompact(object sender, RoutedEventArgs e)
    {
        if (_compactPage == null && App.Services != null)
        {
            var vm = App.Services.GetRequiredService<CompactViewModel>();
            _compactPage = new CompactPage { DataContext = vm };
            _ = vm.DiscoverAsync();
        }
        ContentArea.Content = _compactPage;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void NavigateToSystemDeepClean(object sender, RoutedEventArgs e)
    {
        if (_systemDeepCleanPage == null && App.Services != null)
        {
            var vm = App.Services.GetRequiredService<SystemDeepCleanViewModel>();
            _systemDeepCleanPage = new SystemDeepCleanPage { DataContext = vm };
            _ = vm.RunAuditAsync();
        }
        ContentArea.Content = _systemDeepCleanPage;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void NavigateToQuarantine(object sender, RoutedEventArgs e)
    {
        if (_quarantinePage == null && App.Services != null)
        {
            var vm = App.Services.GetRequiredService<QuarantineViewModel>();
            _quarantinePage = new QuarantinePage { DataContext = vm };
            _ = vm.LoadItemsAsync();
        }
        ContentArea.Content = _quarantinePage;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void NavigateToPlugins(object sender, RoutedEventArgs e)
    {
        if (_pluginsPage == null && App.Services != null)
        {
            var vm = App.Services.GetRequiredService<PluginsViewModel>();
            _pluginsPage = new PluginsPage { DataContext = vm };
            _ = vm.LoadAndScanAsync();
        }
        ContentArea.Content = _pluginsPage;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void NavigateToRamOptimizer(object sender, RoutedEventArgs e)
    {
        if (_ramOptimizerPage == null && App.Services != null)
        {
            var vm = App.Services.GetRequiredService<RamOptimizerViewModel>();
            _ramOptimizerPage = new RamOptimizerPage(vm);
        }
        ContentArea.Content = _ramOptimizerPage;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void NavigateToNetworkOptimizer(object sender, RoutedEventArgs e)
    {
        if (_networkOptimizerPage == null && App.Services != null)
        {
            var vm = App.Services.GetRequiredService<NetworkOptimizerViewModel>();
            _networkOptimizerPage = new NetworkOptimizerPage(vm);
        }
        ContentArea.Content = _networkOptimizerPage;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void NavigateToPrivacyDebloat(object sender, RoutedEventArgs e)
    {
        if (_privacyDebloatPage == null && App.Services != null)
        {
            var vm = App.Services.GetRequiredService<PrivacyDebloatViewModel>();
            _privacyDebloatPage = new PrivacyDebloatPage { DataContext = vm };
        }
        ContentArea.Content = _privacyDebloatPage;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void NavigateToServicesOptimizer(object sender, RoutedEventArgs e)
    {
        if (_servicesOptimizerPage == null && App.Services != null)
        {
            var vm = App.Services.GetRequiredService<ServicesOptimizerViewModel>();
            _servicesOptimizerPage = new ServicesOptimizerPage { DataContext = vm };
        }
        ContentArea.Content = _servicesOptimizerPage;
        MainView.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Visible;
    }

    private void ShowMainView()
    {
        if (MainView != null)
            MainView.Visibility = Visibility.Visible;
        if (ContentArea != null)
            ContentArea.Visibility = Visibility.Collapsed;
    }

    private void ShowAbout_Click(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }

    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        var themeManager = App.Services?.GetService<SmartCleaner.App.Services.ThemeManager>();
        themeManager?.ToggleTheme();
    }
}