using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;
// UseWindowsForms тянет System.Windows.Forms.ICommandExecutor — снимаем
// неоднозначность в пользу контракта исполнителя команд
using ICommandExecutor = SmartCleaner.Core.Cleaning.ICommandExecutor;

namespace SmartCleaner.Core.ServicesOpt;

public class WindowsServicesOptimizer
{
    private static readonly string BackupFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SmartCleaner",
        "services_backup.json");

    private readonly List<WindowsServiceItem> _knownServices =
    [
        new()
        {
            ServiceName = "Fax",
            DisplayName = "Служба факсов (Fax)",
            Description = "Отправка и прием факсов. Бесполезна на 99.9% современных домашних и игровых ПК.",
            Category = "Оборудование",
            RiskLevel = ServiceRiskLevel.SafeToDisable
        },
        new()
        {
            ServiceName = "RemoteRegistry",
            DisplayName = "Дистанционный реестр (RemoteRegistry)",
            Description = "Позволяет удаленным пользователям изменять системный реестр. Отключение повышает безопасность.",
            Category = "Сеть и удаленный доступ",
            RiskLevel = ServiceRiskLevel.SafeToDisable
        },
        new()
        {
            ServiceName = "RetailDemo",
            DisplayName = "Служба демонстрации (RetailDemo)",
            Description = "Режим витрины для демонстрации возможностей Windows в магазинах электроники.",
            Category = "Система",
            RiskLevel = ServiceRiskLevel.SafeToDisable
        },
        new()
        {
            ServiceName = "MapsBroker",
            DisplayName = "Диспетчер скачанных карт (MapsBroker)",
            Description = "Фоновая синхронизация и доступ к автономным офлайн-картам Windows Maps.",
            Category = "Система",
            RiskLevel = ServiceRiskLevel.SafeToDisable
        },
        new()
        {
            ServiceName = "DiagTrack",
            DisplayName = "Телеметрия и сбор событий (DiagTrack)",
            Description = "Connected User Experiences and Telemetry. Сбор и передача логов на сервера Microsoft.",
            Category = "Диагностика",
            RiskLevel = ServiceRiskLevel.SafeToDisable
        },
        new()
        {
            ServiceName = "dmwappushservice",
            DisplayName = "Маршрутизация WAP Push (dmwappushservice)",
            Description = "Служба маршрутизации push-сообщений телеметрии и диагностических данных.",
            Category = "Диагностика",
            RiskLevel = ServiceRiskLevel.SafeToDisable
        },
        new()
        {
            ServiceName = "AllJoynRouter",
            DisplayName = "Маршрутизатор AllJoyn (AllJoynRouter)",
            Description = "Служба взаимодействия с устройствами умного дома IoT по протоколу AllJoyn.",
            Category = "Сеть и удаленный доступ",
            RiskLevel = ServiceRiskLevel.SafeToDisable
        },
        new()
        {
            ServiceName = "WerSvc",
            DisplayName = "Служба отчетов об ошибках (WerSvc)",
            Description = "Формирование и фоновая отправка дампов и отчетов о крашах программ.",
            Category = "Диагностика",
            RiskLevel = ServiceRiskLevel.SafeToDisable
        },
        new()
        {
            ServiceName = "SysMain",
            DisplayName = "Оптимизация запуска SysMain (SuperFetch)",
            Description = "Кэширование часто используемых программ в RAM. На быстрых NVMe/SSD и в играх может вызывать микрофризы.",
            Category = "Система",
            RiskLevel = ServiceRiskLevel.Moderate
        },
        new()
        {
            ServiceName = "WSearch",
            DisplayName = "Индексатор Windows Search (WSearch)",
            Description = "Индексация содержимого файлов на дисках. В тяжелых играх периодически нагружает SSD/дисковый стек.",
            Category = "Система",
            RiskLevel = ServiceRiskLevel.Moderate
        },
        new()
        {
            ServiceName = "WbioSrvc",
            DisplayName = "Биометрическая служба (WbioSrvc)",
            Description = "Обеспечивает работу сканера отпечатков пальцев и распознавания лица Windows Hello.",
            Category = "Оборудование",
            RiskLevel = ServiceRiskLevel.Moderate
        },
        new()
        {
            ServiceName = "XblAuthManager",
            DisplayName = "Аутентификация Xbox Live (XblAuthManager)",
            Description = "Управление учетными записями Xbox Live. Не требуется, если вы не играете в игры из Microsoft Store.",
            Category = "Xbox и игры",
            RiskLevel = ServiceRiskLevel.Moderate
        },
        new()
        {
            ServiceName = "XboxGipSvc",
            DisplayName = "Аксессуары Xbox (XboxGipSvc)",
            Description = "Поддержка беспроводных геймпадов и аксессуаров Xbox. Не требуется при игре с мыши/клавиатуры.",
            Category = "Xbox и игры",
            RiskLevel = ServiceRiskLevel.Moderate
        },
        new()
        {
            ServiceName = "Spooler",
            DisplayName = "Диспетчер печати (Spooler)",
            Description = "Очередь печати документов. Если к компьютеру не подключен принтер, служба не используется.",
            Category = "Оборудование",
            RiskLevel = ServiceRiskLevel.Moderate
        }
    ];

    // Таймауты утилит (дисциплина дней 4-5): конфигурация и запрос состояния —
    // быстрые операции; остановка службы может тянуть зависимые — ей выделяется больше.
    private static readonly TimeSpan ScConfigTimeout = TimeSpan.FromMilliseconds(3000);
    private static readonly TimeSpan ScQueryTimeout = TimeSpan.FromMilliseconds(3000);
    private static readonly TimeSpan NetStopTimeout = TimeSpan.FromMilliseconds(10000);

    private readonly ICommandExecutor _commandExecutor;
    private readonly ILogger _logger;

    /// <summary>
    /// Создаёт оптимизатор поверх реального исполнителя команд (День 17, срез A).
    /// Необязательный исполнитель — шов для детерминированных тестов: стаб
    /// фиксирует команды (полное имя утилиты, аргументы, таймаут).
    /// DI-регистрация исполнителя — День 19. Необязательный ILogger —
    /// диагностика в Release (День 20).
    /// </summary>
    public WindowsServicesOptimizer(ICommandExecutor? commandExecutor = null, ILogger? logger = null)
    {
        _commandExecutor = commandExecutor ?? new ProcessCommandExecutor();
        _logger = logger ?? NullLogger.Instance;
    }

    public IReadOnlyList<WindowsServiceItem> GetKnownServices() => _knownServices;

    public async Task<List<WindowsServiceItem>> ScanServicesAsync()
    {
        return await Task.Run(async () =>
        {
            var result = new List<WindowsServiceItem>();
            foreach (var item in _knownServices)
            {
                var clone = CloneService(item);
                await ReadServiceStateAsync(clone);
                result.Add(clone);
            }
            return result;
        });
    }

    public async Task<bool> SetServiceStartupAsync(string serviceName, ServiceStartupType startupType)
    {
        return await Task.Run(async () =>
        {
            try
            {
                SaveBackupBeforeChange(serviceName);

                string startArg = startupType switch
                {
                    ServiceStartupType.Automatic => "auto",
                    ServiceStartupType.Manual => "demand",
                    ServiceStartupType.Disabled => "disabled",
                    _ => "demand"
                };

                // «start=» — отдельный аргумент: у sc.exe между «start=» и значением
                // обязан быть пробел; ArgumentList воспроизводит прежнюю командную строку
                var config = await _commandExecutor.ExecuteAsync(new CommandExecutionRequest
                {
                    // Абсолютный путь из системного каталога: запуск по неквалифицированному
                    // имени ищет exe в каталоге приложения — binary planting (M7, День 16б)
                    FileName = SystemToolLocator.GetScPath(),
                    Arguments = ["config", serviceName, "start=", startArg],
                    WorkingDirectory = string.Empty,
                    Timeout = ScConfigTimeout
                });
                if (config.ExitCode != 0)
                {
                    _logger.LogWarning("sc config '{ServiceName}' failed (exit {ExitCode}): {Output}",
                        serviceName, config.ExitCode, ToolOutput(config));
                    return false;
                }

                if (startupType == ServiceStartupType.Disabled)
                {
                    var stop = await _commandExecutor.ExecuteAsync(new CommandExecutionRequest
                    {
                        // Абсолютный путь (binary planting, M7): см. sc config выше
                        FileName = SystemToolLocator.GetNetPath(),
                        Arguments = ["stop", serviceName, "/y"],
                        WorkingDirectory = string.Empty,
                        Timeout = NetStopTimeout
                    });
                    if (stop.ExitCode != 0)
                    {
                        // net stop завершается ошибкой и для незапущенной службы — это не сбой,
                        // если служба подтверждённо не работает (цель «остановлена» достигнута)
                        var stillRunning = await IsServiceRunningAsync(serviceName);
                        if (stillRunning != false)
                        {
                            _logger.LogWarning("net stop '{ServiceName}' failed (exit {ExitCode}): {Output}",
                                serviceName, stop.ExitCode, ToolOutput(stop));
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SetServiceStartupAsync '{ServiceName}' failed: {Error}", serviceName, ex.Message);
                return false;
            }
        });
    }

    public async Task<int> ApplyProfileAsync(ServiceProfileType profileType, IProgress<string>? progress = null)
    {
        var targetServices = GetProfileTargetServices(profileType);
        int modifiedCount = 0;

        foreach (var svcName in targetServices)
        {
            progress?.Report($"Настройка службы {svcName}...");
            if (await SetServiceStartupAsync(svcName, ServiceStartupType.Disabled))
            {
                modifiedCount++;
            }
        }

        return modifiedCount;
    }

    public async Task<int> RestoreDefaultServicesAsync(IProgress<string>? progress = null)
    {
        int restoredCount = 0;
        var backup = LoadBackup();

        foreach (var svc in _knownServices)
        {
            progress?.Report($"Восстановление службы {svc.ServiceName}...");
            var targetType = ServiceStartupType.Manual;

            if (backup.TryGetValue(svc.ServiceName, out var savedTypeInt) && Enum.IsDefined(typeof(ServiceStartupType), savedTypeInt))
            {
                targetType = (ServiceStartupType)savedTypeInt;
            }
            else
            {
                // Fallback default
                targetType = svc.RiskLevel == ServiceRiskLevel.SafeToDisable ? ServiceStartupType.Manual : ServiceStartupType.Automatic;
            }

            if (await SetServiceStartupAsync(svc.ServiceName, targetType))
            {
                restoredCount++;
            }
        }

        return restoredCount;
    }

    public static List<string> GetProfileTargetServices(ServiceProfileType profileType) => profileType switch
    {
        ServiceProfileType.Gaming =>
        [
            "SysMain", "DiagTrack", "dmwappushservice", "WerSvc",
            "Fax", "RemoteRegistry", "RetailDemo", "MapsBroker", "AllJoynRouter"
        ],
        ServiceProfileType.Balanced =>
        [
            "Fax", "RemoteRegistry", "RetailDemo", "MapsBroker", "DiagTrack", "dmwappushservice"
        ],
        ServiceProfileType.Workstation =>
        [
            "Fax", "RemoteRegistry", "RetailDemo", "DiagTrack", "AllJoynRouter"
        ],
        _ => []
    };

    private async Task ReadServiceStateAsync(WindowsServiceItem item)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{item.ServiceName}");
            if (key != null)
            {
                var startVal = key.GetValue("Start");
                if (startVal is int startInt)
                {
                    item.StartupType = startInt switch
                    {
                        2 => ServiceStartupType.Automatic,
                        3 => ServiceStartupType.Manual,
                        4 => ServiceStartupType.Disabled,
                        _ => ServiceStartupType.Unknown
                    };
                }
            }

            // Check running status via sc query — теперь через исполнителя,
            // с таймаутом: прежнее ReadToEnd без таймаута мог зависнуть навсегда
            var query = await _commandExecutor.ExecuteAsync(new CommandExecutionRequest
            {
                // Абсолютный путь из системного каталога — binary planting (M7)
                FileName = SystemToolLocator.GetScPath(),
                Arguments = ["query", item.ServiceName],
                WorkingDirectory = string.Empty,
                Timeout = ScQueryTimeout
            });
            if (query.ExitCode == 0)
            {
                item.IsRunning = query.StandardOutput.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReadServiceState '{ServiceName}' failed: {Error}", item.ServiceName, ex.Message);
        }
    }

    // Вывод утилиты для диагностики: stderr приоритетнее — sc.exe пишет ошибки
    // в stdout, net.exe — в stderr (дисциплина дней 4-5, сохранённая поверх
    // результатов исполнителя команд).
    private static string ToolOutput(CommandExecutionResult result) =>
        string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput.Trim() : result.StandardError.Trim();

    // Определяет по «sc query», запущена ли служба. null — состояние неизвестно (ошибка запроса).
    private async Task<bool?> IsServiceRunningAsync(string serviceName)
    {
        try
        {
            var query = await _commandExecutor.ExecuteAsync(new CommandExecutionRequest
            {
                // Абсолютный путь из системного каталога — binary planting (M7)
                FileName = SystemToolLocator.GetScPath(),
                Arguments = ["query", serviceName],
                WorkingDirectory = string.Empty,
                Timeout = ScQueryTimeout
            });
            if (query.ExitCode != 0)
            {
                return null;
            }

            return ToolOutput(query).Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "sc query '{ServiceName}' failed: {Error}", serviceName, ex.Message);
            return null;
        }
    }

    private void SaveBackupBeforeChange(string serviceName)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BackupFilePath)!);
            var backup = LoadBackup();

            if (!backup.ContainsKey(serviceName))
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
                if (key?.GetValue("Start") is int startInt)
                {
                    backup[serviceName] = startInt;
                    File.WriteAllText(BackupFilePath, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
        }
        catch (Exception ex)
        {
            // Backup should never block, но причина сбоя должна быть видна
            _logger.LogWarning(ex, "SaveBackupBeforeChange '{ServiceName}' failed: {Error}", serviceName, ex.Message);
        }
    }

    private Dictionary<string, int> LoadBackup()
    {
        try
        {
            if (File.Exists(BackupFilePath))
            {
                var json = File.ReadAllText(BackupFilePath);
                return JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? [];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LoadBackup failed: {Error}", ex.Message);
        }
        return [];
    }

    private static WindowsServiceItem CloneService(WindowsServiceItem s) => new()
    {
        ServiceName = s.ServiceName,
        DisplayName = s.DisplayName,
        Description = s.Description,
        Category = s.Category,
        RiskLevel = s.RiskLevel,
        StartupType = s.StartupType,
        IsRunning = s.IsRunning
    };
}
