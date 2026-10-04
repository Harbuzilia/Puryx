using SmartCleaner.Core.Helpers;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

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

    public IReadOnlyList<WindowsServiceItem> GetKnownServices() => _knownServices;

    public async Task<List<WindowsServiceItem>> ScanServicesAsync()
    {
        return await Task.Run(() =>
        {
            var result = new List<WindowsServiceItem>();
            foreach (var item in _knownServices)
            {
                var clone = CloneService(item);
                ReadServiceState(clone);
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

                var psiConfig = new ProcessStartInfo
                {
                    // Абсолютный путь из системного каталога: запуск по неквалифицированному
                    // имени ищет exe в каталоге приложения — binary planting (M7, День 16б)
                    FileName = SystemToolLocator.GetScPath(),
                    Arguments = $"config {serviceName} start= {startArg}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var config = await RunToolAsync(psiConfig, 3000);
                if (config.ExitCode != 0)
                {
                    Debug.WriteLine($"[WindowsServicesOptimizer] sc config '{serviceName}' failed (exit {config.ExitCode}): {config.Output}");
                    return false;
                }

                if (startupType == ServiceStartupType.Disabled)
                {
                    var psiStop = new ProcessStartInfo
                    {
                        // Абсолютный путь (binary planting, M7): см. psiConfig выше
                        FileName = SystemToolLocator.GetNetPath(),
                        Arguments = $"stop {serviceName} /y",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    var stop = await RunToolAsync(psiStop, 10000);
                    if (stop.ExitCode != 0)
                    {
                        // net stop завершается ошибкой и для незапущенной службы — это не сбой,
                        // если служба подтверждённо не работает (цель «остановлена» достигнута)
                        var stillRunning = await IsServiceRunningAsync(serviceName);
                        if (stillRunning != false)
                        {
                            Debug.WriteLine($"[WindowsServicesOptimizer] net stop '{serviceName}' failed (exit {stop.ExitCode}): {stop.Output}");
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsServicesOptimizer] SetServiceStartupAsync '{serviceName}' failed: {ex.Message}");
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

    private static void ReadServiceState(WindowsServiceItem item)
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

            // Check running status via sc query
            var psi = new ProcessStartInfo
            {
                // Абсолютный путь из системного каталога — binary planting (M7)
                FileName = SystemToolLocator.GetScPath(),
                Arguments = $"query {item.ServiceName}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                item.IsRunning = output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowsServicesOptimizer] ReadServiceState '{item.ServiceName}' failed: {ex.Message}");
        }
    }

    // Запускает системную утилиту и возвращает фактический код возврата и вывод
    // (stderr приоритетнее: sc.exe пишет ошибки в stdout, net.exe — в stderr).
    // Таймаут — тоже отказ: никакого «успеха по молчанию».
    private static async Task<(int ExitCode, bool TimedOut, string Output)> RunToolAsync(ProcessStartInfo psi, int timeoutMs)
    {
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        try
        {
            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                return (-1, false, $"не удалось запустить «{psi.FileName}»");
            }

            // Потоки читаем до ожидания выхода, чтобы заполненный буфер не заблокировал утилиту
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
            }
            catch (TimeoutException)
            {
                KillProcessTree(process);
                return (-1, true, $"«{psi.FileName}» не завершилась за {timeoutMs} мс");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var output = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return (process.ExitCode, false, output.Trim());
        }
        catch (Exception ex)
        {
            return (-1, false, $"«{psi.FileName}»: {ex.Message}");
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowsServicesOptimizer] Kill timed-out process failed: {ex.Message}");
        }
    }

    // Определяет по «sc query», запущена ли служба. null — состояние неизвестно (ошибка запроса).
    private static async Task<bool?> IsServiceRunningAsync(string serviceName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                // Абсолютный путь из системного каталога — binary planting (M7)
                FileName = SystemToolLocator.GetScPath(),
                Arguments = $"query {serviceName}",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            var query = await RunToolAsync(psi, 3000);
            if (query.ExitCode != 0)
            {
                return null;
            }

            return query.Output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowsServicesOptimizer] sc query '{serviceName}' failed: {ex.Message}");
            return null;
        }
    }

    private static void SaveBackupBeforeChange(string serviceName)
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
            Debug.WriteLine($"[WindowsServicesOptimizer] SaveBackupBeforeChange '{serviceName}' failed: {ex.Message}");
        }
    }

    private static Dictionary<string, int> LoadBackup()
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
            Debug.WriteLine($"[WindowsServicesOptimizer] LoadBackup failed: {ex.Message}");
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
