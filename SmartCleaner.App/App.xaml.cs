using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartCleaner.App.ViewModels;
using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Knowledge;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
using SmartCleaner.Core.Scheduler;
using SmartCleaner.Core.Scanning;
using SmartCleaner.Core.Scanning.Scanners;
using SmartCleaner.Core.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace SmartCleaner.App;

public partial class App : Application
{
    private const string ElevatedCleanArgument = "--elevated-clean";
    private const string ElevatedAuthTokenArgument = "--auth-token";
    private static readonly TimeSpan ElevatedRequestMaxAge = TimeSpan.FromMinutes(10);

    private ServiceProvider? _serviceProvider;

    public static IServiceProvider? Services { get; private set; }

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        // Глобальные обработчики необработанных исключений
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        try
        {
            if (TryGetElevatedRequest(e.Args, out var requestPath, out var authToken))
            {
                var exitCode = await RunElevatedCleanupAsync(requestPath, authToken);
                Shutdown(exitCode);
                return;
            }

            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
            Services = _serviceProvider;

            var themeManager = _serviceProvider.GetRequiredService<SmartCleaner.App.Services.ThemeManager>();
            themeManager.Initialize();

            // M5: восстановление после краша с активным Game Boost —
            // state-файл существует, а процесс-владелец мёртв => вернуть службы и план питания
            try
            {
                await _serviceProvider.GetRequiredService<SmartCleaner.Core.SystemOpt.GameBoostService>()
                    .TryRecoverFromCrashAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] GameBoost crash recovery failed: {ex.Message}");
            }

            // Обработка --auto-clean (от планировщика): тихая очистка без GUI
            if (TryGetAutoCleanArgs(e.Args, out var profile))
            {
                await RunAutoCleanAsync(profile);
                Shutdown(0);
                return;
            }

            var mainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<MainViewModel>()
            };
            mainWindow.Show();

            // Обработка --scan-path (от контекстного меню): открыть приложение и вставить путь
            if (TryGetScanPath(e.Args, out var scanPath))
            {
                if (mainWindow.DataContext is MainViewModel vm)
                {
                    await vm.ScanDroppedPathsAsync([scanPath]);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка запуска:\n\n{ex}\n\nInner: {ex.InnerException}",
                "Puryx - Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>
    /// Обработчик необработанных исключений UI-потока
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        MessageBox.Show(
            $"Произошла непредвиденная ошибка:\n\n{e.Exception.Message}\n\nПодробности сохранены в crash.log",
            "Puryx — Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    /// <summary>
    /// Обработчик необработанных исключений в Task (фоновые потоки)
    /// </summary>
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        e.SetObserved();
    }

    /// <summary>
    /// Обработчик необработанных исключений в AppDomain
    /// </summary>
    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogCrash(ex);
        }
    }

    /// <summary>
    /// Записать крэш-лог в доступную для записи директорию:
    /// рядом с exe в portable-режиме, иначе в %APPDATA%\SmartCleaner
    /// (BaseDirectory под Program Files доступен только на чтение)
    /// </summary>
    private static void LogCrash(Exception ex)
    {
        try
        {
            string baseDir;
            var portableMarker = Path.Combine(AppContext.BaseDirectory, "portable.txt");
            if (File.Exists(portableMarker))
            {
                baseDir = AppContext.BaseDirectory;
            }
            else
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                baseDir = Path.Combine(appData, "SmartCleaner");
                Directory.CreateDirectory(baseDir);
            }

            var logPath = Path.Combine(baseDir, "crash.log");
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n";
            File.AppendAllText(logPath, entry);
        }
        catch (Exception logEx)
        {
            // Логирование не должно рушить приложение — хотя бы в Debug-выход
            Debug.WriteLine($"[CrashLog] Failed to write crash log: {logEx.Message}");
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Logging (День 20, ROADMAP 261-267): Debug-провайдер записывает логи
        // ILogger туда, куда смотрит отладчик. Факт пакета 8.0.1: DebugLogger
        // пишет только при подключённом отладчике (IsEnabled == Debugger.IsAttached)
        // — в VS Output виден и в Release-сборке; без отладчика провайдер
        // справедливо молчит. Файловый провайдер продакшен-контура — вне Дня 20.
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        // День 20 (ROADMAP 266): сервисы Core принимают ILogger необязательным
        // ctor-параметром. AddLogging регистрирует открытый generic ILogger<>,
        // поэтому PrivacyDebloatService получает ILogger<T> автоматически;
        // для не-generic ILogger категория создаётся фабрикой по полному
        // имени типа сервиса
        static ILogger LoggerFor(IServiceProvider sp, Type serviceType) =>
            sp.GetRequiredService<ILoggerFactory>().CreateLogger(serviceType.FullName ?? serviceType.Name);

        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<SmartCleaner.App.Services.ThemeManager>();
        services.AddSingleton<ISafetyService, SafetyService>();
        services.AddSingleton<IKnowledgeBase, KnowledgeBase>();
        services.AddSingleton<ICleaningService, CleaningService>();
        services.AddSingleton<IPackageMaintenanceService, PackageMaintenanceService>();
        services.AddSingleton<LoggingService>();

        // День 19 (ROADMAP 258): единый исполнитель команд в DI. Сервисы,
        // переведённые на контракт ICommandExecutor в днях 17-19 (сервисы/сеть/
        // приватность/буст/WinSxS/Compact/SQLite/uninstaller/startup/scheduler),
        // принимают его необязательным ctor-параметром — регистрация «включает»
        // шов: без неё сервисы создаются с собственным ProcessCommandExecutor,
        // с ней — получают единый инстанс из контейнера. День 20: сюда же
        // прокидывается ILogger для диагностики Release
        services.AddSingleton<SmartCleaner.Core.Cleaning.ICommandExecutor>(sp =>
            new SmartCleaner.Core.Cleaning.ProcessCommandExecutor(
                LoggerFor(sp, typeof(SmartCleaner.Core.Cleaning.ProcessCommandExecutor))));

        services.AddSingleton<ScanConfiguration>();

        services.AddSingleton<IScannerStrategy, PackageCacheScanner>();
        services.AddSingleton<IScannerStrategy, SystemScanner>();
        services.AddSingleton<IScannerStrategy, BrowserScanner>();
        services.AddSingleton<IScannerStrategy, GamesScanner>();
        services.AddSingleton<IScannerStrategy, AIAgentsScanner>();
        services.AddSingleton<IScannerStrategy, NodeModulesScanner>();
        services.AddSingleton<IScannerStrategy, NpmPackagesScanner>();
        services.AddSingleton<IScannerStrategy, PythonPackagesScanner>();
        services.AddSingleton<IScannerStrategy, DiskUsageScanner>();
        services.AddSingleton<IScannerStrategy, DotnetArtifactsScanner>();
        services.AddSingleton<IScannerStrategy, PythonVenvScanner>();
        services.AddSingleton<IScannerStrategy, DockerScanner>();
        services.AddSingleton<IScannerStrategy, CustomFolderScanner>();
        services.AddSingleton<IScannerStrategy, AppCacheScanner>();
        services.AddSingleton<IScannerStrategy, DevSuperScanner>();
        services.AddSingleton<IScannerStrategy, SmartCleaner.Core.Scanning.Scanners.ShaderCacheScanner>();

        services.AddSingleton<SmartCleaner.Core.Models.CleaningStatsServiceLegacy>();

        // Duplicate Engine (separate module)
        services.AddSingleton<SmartCleaner.Core.Duplicates.DuplicateEngine>();
        services.AddSingleton<SmartCleaner.Core.Duplicates.DeletionLogService>();

        // Startup Analyzer (separate module)
        services.AddSingleton<SmartCleaner.Core.Startup.StartupEngine>();

        // DiskMap (separate module)
        services.AddSingleton<SmartCleaner.Core.DiskMap.DiskMapEngine>();

        // Large Files Finder
        services.AddSingleton<SmartCleaner.Core.LargeFiles.LargeFilesEngine>();

        // Scheduler
        services.AddSingleton<SmartCleaner.Core.Scheduler.CleaningSchedulerService>();

        // Stats / Dashboard
        services.AddSingleton<SmartCleaner.Core.Stats.CleaningStatsService>();

        // Context Menu
        services.AddSingleton<SmartCleaner.Core.Shell.ContextMenuService>();

        // CLI & AI Inspector (Deliter module)
        services.AddSingleton<SmartCleaner.Core.CliInspector.PathEnvironmentService>();
        services.AddSingleton<SmartCleaner.Core.CliInspector.CliInspectorEngine>();

        // Tier Features Services
        services.AddSingleton<SmartCleaner.Core.Uninstaller.LeftoverHunter>();
        services.AddSingleton<SmartCleaner.Core.Uninstaller.UninstallerEngine>();
        services.AddSingleton<SmartCleaner.Core.Compression.CompactEngine>();
        services.AddSingleton<SmartCleaner.Core.WinSxS.WinSxSEngine>();
        services.AddSingleton<SmartCleaner.Core.WinSxS.DriverStoreCleaner>();
        services.AddSingleton<SmartCleaner.Core.Safety.QuarantineService>(sp =>
            new SmartCleaner.Core.Safety.QuarantineService(
                sp.GetRequiredService<IConfigService>(),
                LoggerFor(sp, typeof(SmartCleaner.Core.Safety.QuarantineService))));
        services.AddSingleton<SmartCleaner.Core.Plugins.PluginEngine>();
        services.AddSingleton<SmartCleaner.Core.AiAssistant.NaturalLanguageQueryEngine>();

        // God-Tier & Master Services
        services.AddSingleton<SmartCleaner.Core.Optimization.SqliteCompactorService>();
        services.AddSingleton<SmartCleaner.Core.SystemOpt.RamOptimizerService>(sp =>
            new SmartCleaner.Core.SystemOpt.RamOptimizerService(
                LoggerFor(sp, typeof(SmartCleaner.Core.SystemOpt.RamOptimizerService))));
        services.AddSingleton<SmartCleaner.Core.SystemOpt.IGameBoostSystemOperations>(sp =>
            new SmartCleaner.Core.SystemOpt.StandardGameBoostSystemOperations(
                sp.GetRequiredService<SmartCleaner.Core.Cleaning.ICommandExecutor>(),
                LoggerFor(sp, typeof(SmartCleaner.Core.SystemOpt.StandardGameBoostSystemOperations))));
        services.AddSingleton<SmartCleaner.Core.SystemOpt.GameBoostService>(sp =>
            new SmartCleaner.Core.SystemOpt.GameBoostService(
                sp.GetRequiredService<SmartCleaner.Core.SystemOpt.RamOptimizerService>(),
                sp.GetRequiredService<IConfigService>(),
                sp.GetRequiredService<SmartCleaner.Core.SystemOpt.IGameBoostSystemOperations>(),
                LoggerFor(sp, typeof(SmartCleaner.Core.SystemOpt.GameBoostService))));
        services.AddSingleton<SmartCleaner.Core.DiskHealth.DiskHealthService>();
        services.AddSingleton<SmartCleaner.Core.Safety.FileShredderService>(sp =>
            new SmartCleaner.Core.Safety.FileShredderService(
                sp.GetRequiredService<ISafetyService>(),
                LoggerFor(sp, typeof(SmartCleaner.Core.Safety.FileShredderService))));
        services.AddSingleton<SmartCleaner.Core.Network.NetworkOptimizerService>(sp =>
            new SmartCleaner.Core.Network.NetworkOptimizerService(
                sp.GetRequiredService<SmartCleaner.Core.Cleaning.ICommandExecutor>(),
                LoggerFor(sp, typeof(SmartCleaner.Core.Network.NetworkOptimizerService))));
        services.AddSingleton<SmartCleaner.Core.Shell.ExplorerContextMenuManager>();
        services.AddSingleton<SmartCleaner.Core.Reporting.SystemReportGenerator>();
        services.AddSingleton<SmartCleaner.App.Services.AudioFeedbackService>();
        // ILogger<PrivacyDebloatService> резолвится AddLogging автоматически
        // (открытый generic) — фабрика не нужна
        services.AddSingleton<SmartCleaner.Core.Privacy.PrivacyDebloatService>();
        services.AddSingleton<SmartCleaner.Core.ServicesOpt.WindowsServicesOptimizer>(sp =>
            new SmartCleaner.Core.ServicesOpt.WindowsServicesOptimizer(
                sp.GetRequiredService<SmartCleaner.Core.Cleaning.ICommandExecutor>(),
                LoggerFor(sp, typeof(SmartCleaner.Core.ServicesOpt.WindowsServicesOptimizer))));

        services.AddTransient<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<DiscoveryViewModel>();
        services.AddTransient<DuplicatesViewModel>();
        services.AddTransient<StartupViewModel>();
        services.AddTransient<DiskMapViewModel>();
        services.AddTransient<LargeFilesViewModel>();
        services.AddTransient<SchedulerViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<CliInspectorViewModel>();
        services.AddTransient<UninstallerViewModel>();
        services.AddTransient<CompactViewModel>();
        services.AddTransient<SystemDeepCleanViewModel>();
        services.AddTransient<QuarantineViewModel>();
        services.AddTransient<PluginsViewModel>();
        services.AddTransient<AiAssistantViewModel>();
        services.AddTransient<RamOptimizerViewModel>();
        services.AddTransient<NetworkOptimizerViewModel>();
        services.AddTransient<PrivacyDebloatViewModel>();
        services.AddTransient<ServicesOptimizerViewModel>();
    }

    private static bool TryGetElevatedRequest(IReadOnlyList<string> args, out string requestPath, out string authToken)
    {
        requestPath = string.Empty;
        authToken = string.Empty;

        if (args.Count < 4 || !string.Equals(args[0], ElevatedCleanArgument, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        requestPath = args[1];

        for (var i = 2; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], ElevatedAuthTokenArgument, StringComparison.OrdinalIgnoreCase))
            {
                authToken = args[i + 1];
                break;
            }
        }

        return !string.IsNullOrWhiteSpace(requestPath) && !string.IsNullOrWhiteSpace(authToken);
    }

    private async Task<int> RunElevatedCleanupAsync(string requestPath, string authToken)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        // День 20: логгер для статического ElevatedCleanRequestFile — записи
        // валидации/очистки запроса живы в Release (Diagnostic-вывод вырезан)
        var elevatedLogger = provider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("SmartCleaner.App.ElevatedClean");
        var safetyService = provider.GetRequiredService<ISafetyService>();

        try
        {
            var request = await ElevatedCleanRequestFile.ReadValidatedAsync(requestPath, authToken, ElevatedRequestMaxAge, logger: elevatedLogger);
            if (request is null || !ElevatedCleanTargetPolicy.ValidateContract(request.Policy))
            {
                var invalidResult = new ElevatedCleanExecutionResult
                {
                    DeletedCount = 0,
                    FailedCount = 1,
                    SkippedCount = 0,
                    Errors =
                    [
                        new CleaningError
                        {
                            Path = requestPath,
                            Message = "Некорректный или поддельный elevated-clean запрос"
                        }
                    ]
                };

                if (ElevatedCleanRequestFile.IsSafeRequestPath(requestPath))
                {
                    await ElevatedCleanRequestFile.WriteResultAsync(requestPath, invalidResult);
                }

                return 2;
            }

            var deletedCount = 0;
            var failedCount = 0;
            var skippedCount = 0;
            var errors = new List<CleaningError>();

            foreach (var item in request.Items)
            {
                var fullPath = Path.GetFullPath(item.Path);
                if (!ElevatedCleanTargetPolicy.IsAllowedTarget(fullPath))
                {
                    skippedCount++;
                    errors.Add(new CleaningError
                    {
                        Path = fullPath,
                        Message = "Путь не соответствует политике elevated-clean"
                    });
                    continue;
                }

                // Реальная категория риска из запроса — защищённый период
                // для UserData применяется и в elevated-режиме
                var itemRisk = Enum.TryParse<RiskCategory>(item.Risk, out var parsedRisk)
                    ? parsedRisk
                    : RiskCategory.PerformanceCache;

                var scannedItem = new ScannedItem
                {
                    Path = fullPath,
                    IsDirectory = item.IsDirectory,
                    Size = item.Size,
                    LastAccess = DateTime.MinValue,
                    Risk = itemRisk,
                    Description = "elevated-clean"
                };

                var validation = safetyService.ValidateForDeletion(scannedItem);
                if (!validation.CanDelete || !validation.RequiresElevation)
                {
                    skippedCount++;
                    errors.Add(new CleaningError
                    {
                        Path = fullPath,
                        Message = validation.BlockReason ?? "Путь отклонён политикой безопасности",
                        RequiresElevation = validation.RequiresElevation
                    });
                    continue;
                }

                if (!item.IsDirectory && !File.Exists(fullPath))
                {
                    skippedCount++;
                    continue;
                }

                if (item.IsDirectory && !Directory.Exists(fullPath))
                {
                    skippedCount++;
                    continue;
                }

                try
                {
                    if (item.IsDirectory)
                    {
                        Directory.Delete(fullPath, recursive: true);
                    }
                    else
                    {
                        File.Delete(fullPath);
                    }

                    deletedCount++;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    var message = $"{ex.GetType().Name}: {ex.Message}";
                    Debug.WriteLine($"[elevated-clean] failed to delete '{fullPath}': {message}");
                    errors.Add(new CleaningError
                    {
                        Path = fullPath,
                        Message = message,
                        RequiresElevation = true
                    });
                }
            }

            var result = new ElevatedCleanExecutionResult
            {
                DeletedCount = deletedCount,
                FailedCount = failedCount,
                SkippedCount = skippedCount,
                Errors = errors
            };

            await ElevatedCleanRequestFile.WriteResultAsync(requestPath, result);

            return failedCount > 0 ? 4 : 0;
        }
        catch (Exception ex)
        {
            var fatalResult = new ElevatedCleanExecutionResult
            {
                DeletedCount = 0,
                FailedCount = 1,
                SkippedCount = 0,
                Errors =
                [
                    new CleaningError
                    {
                        Path = requestPath,
                        Message = $"Fatal elevated-clean error: {ex.GetType().Name}: {ex.Message}"
                    }
                ]
            };

            await ElevatedCleanRequestFile.WriteResultAsync(requestPath, fatalResult);
            return 3;
        }
        finally
        {
            ElevatedCleanRequestFile.TryDelete(requestPath, elevatedLogger);
        }
    }

    /// <summary>
    /// Проверяет, передан ли аргумент --scan-path (от контекстного меню).
    /// </summary>
    private static bool TryGetScanPath(IReadOnlyList<string> args, out string path)
    {
        path = string.Empty;
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], "--scan-path", StringComparison.OrdinalIgnoreCase))
            {
                path = args[i + 1].Trim('"');
                return !string.IsNullOrWhiteSpace(path);
            }
        }
        return false;
    }

    /// <summary>
    /// Проверяет, передан ли аргумент --auto-clean (от планировщика).
    /// </summary>
    private static bool TryGetAutoCleanArgs(IReadOnlyList<string> args, out string profile)
    {
        profile = "Быстрая";
        bool found = false;
        for (int i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], "--auto-clean", StringComparison.OrdinalIgnoreCase))
                found = true;
            if (string.Equals(args[i], "--profile", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                profile = args[i + 1];
        }
        return found;
    }

    /// <summary>
    /// Автоматическая очистка без GUI (вызывается планировщиком).
    /// Применяет профиль из аргументов (--profile) к набору сканеров,
    /// сканирует и очищает безопасные элементы.
    /// </summary>
    private async Task RunAutoCleanAsync(string profile)
    {
        try
        {
            if (_serviceProvider == null) return;

            var vm = _serviceProvider.GetRequiredService<MainViewModel>();

            // Профиль из CLI выбирает набор сканеров ДО запуска сканирования
            ApplyAutoCleanProfile(vm, profile);

            // Сканирование выбранным профилем набором сканеров
            if (vm.ScanCommand.CanExecute(null))
                await vm.ScanCommand.ExecuteAsync(null);

            // Автоматическая очистка (только безопасных элементов)
            if (vm.CleanCommand.CanExecute(null))
                await vm.CleanCommand.ExecuteAsync(null);

            Debug.WriteLine("[auto-clean] Completed successfully.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[auto-clean] Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Применяет профиль очистки к чекбоксам сканеров (ScannerOptions) —
    /// той же семантикой, что и MainViewModel.ApplyProfile в UI.
    /// Неизвестный профиль заменяется безопасным дефолтом «Быстрая»
    /// с предупреждением в лог.
    /// </summary>
    private static void ApplyAutoCleanProfile(MainViewModel vm, string profile)
    {
        if (!CleaningProfileMap.TryGetEnabledCategories(profile, out var enabledSet))
        {
            Debug.WriteLine($"[auto-clean] Неизвестный профиль '{profile}', используется '{CleaningProfileMap.Quick}'.");
            profile = CleaningProfileMap.Quick;
            // «Быстрая» всегда известна маппингу
            CleaningProfileMap.TryGetEnabledCategories(profile, out enabledSet);
        }

        vm.SelectedProfile = profile;
        foreach (var option in vm.ScannerOptions)
        {
            option.IsEnabled = enabledSet == null || enabledSet.Contains(option.CategoryName);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
