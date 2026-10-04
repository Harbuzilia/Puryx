using SmartCleaner.Core.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace SmartCleaner.Core.Privacy;

public class PrivacyDebloatService
{
    private static readonly string DefaultBackupFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SmartCleaner",
        "privacy_backup.json");

    private static readonly JsonSerializerOptions BackupJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IRegistryValueStore _registry;
    private readonly string _backupFilePath;
    private readonly ILogger<PrivacyDebloatService>? _logger;

    /// <summary>
    /// Создаёт сервис поверх реального реестра и файла бэкапа в AppData.
    /// Необязательные параметры — шов для детерминированных тестов:
    /// фейковое хранилище значений, путь к файлу бэкапа и логгер.
    /// </summary>
    public PrivacyDebloatService(
        IRegistryValueStore? registry = null,
        string? backupFilePath = null,
        ILogger<PrivacyDebloatService>? logger = null)
    {
        _registry = registry ?? new RegistryValueStore();
        _backupFilePath = backupFilePath ?? DefaultBackupFilePath;
        _logger = logger;
    }

    private readonly List<PrivacyTweakItem> _tweakDefinitions =
    [
        new()
        {
            Id = "telemetry_datacollection",
            Title = "Сбор диагностической телеметрии",
            Description = "Запрещает Windows передавать расширенную телеметрию и данные использования на сервера Microsoft (AllowTelemetry = 0).",
            Category = PrivacyCategory.Telemetry,
            RegistryRoot = "HKLM",
            SubKeyPath = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
            ValueName = "AllowTelemetry",
            DisabledValue = 0,
            DefaultValue = 1
        },
        new()
        {
            Id = "telemetry_diagtrack",
            Title = "Служба телеметрии DiagTrack",
            Description = "Отключает фоновую службу «Connected User Experiences and Telemetry», собирающую системные логи и события.",
            Category = PrivacyCategory.Telemetry,
            ServiceName = "DiagTrack"
        },
        new()
        {
            Id = "telemetry_dmwappush",
            Title = "Служба WAP Push (dmwappushservice)",
            Description = "Отключает службу маршрутизации push-сообщений телеметрии WAP.",
            Category = PrivacyCategory.Telemetry,
            ServiceName = "dmwappushservice"
        },
        new()
        {
            Id = "telemetry_enhanceddiag",
            Title = "Расширенная диагностика приложений",
            Description = "Блокирует отправку дампов памяти и стеков вызовов сторонних приложений при сбоях.",
            Category = PrivacyCategory.Telemetry,
            RegistryRoot = "HKLM",
            SubKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Diagnostics\DiagTrack",
            ValueName = "ShowDiagOptIn",
            DisabledValue = 0,
            DefaultValue = 1
        },
        new()
        {
            Id = "advertising_id",
            Title = "Рекламный идентификатор (Advertising ID)",
            Description = "Запрещает приложениям использовать уникальный ID пользователя для показа персонализированной рекламы.",
            Category = PrivacyCategory.Advertising,
            RegistryRoot = "HKCU",
            SubKeyPath = @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
            ValueName = "Enabled",
            DisabledValue = 0,
            DefaultValue = 1
        },
        new()
        {
            Id = "advertising_bingsearch",
            Title = "Веб-поиск Bing в меню Пуск",
            Description = "Отключает сетевые запросы в Bing при поиске локальных файлов и программ в меню Пуск.",
            Category = PrivacyCategory.Advertising,
            RegistryRoot = "HKCU",
            SubKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Search",
            ValueName = "BingSearchEnabled",
            DisabledValue = 0,
            DefaultValue = 1
        },
        new()
        {
            Id = "advertising_suggestions",
            Title = "Рекомендации в строке поиска Explorer",
            Description = "Отключает всплывающие подсказки и рекламу в окнах Проводника Windows.",
            Category = PrivacyCategory.Advertising,
            RegistryRoot = "HKCU",
            SubKeyPath = @"Software\Policies\Microsoft\Windows\Explorer",
            ValueName = "DisableSearchBoxSuggestions",
            DisabledValue = 1,
            DefaultValue = 0
        },
        new()
        {
            Id = "advertising_tailored",
            Title = "Персонализированный опыт (Tailored Experiences)",
            Description = "Запрещает Microsoft использовать диагностические данные для рекламных рекомендаций.",
            Category = PrivacyCategory.Advertising,
            RegistryRoot = "HKCU",
            SubKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Privacy",
            ValueName = "TailoredExperiencesWithDiagnosticDataEnabled",
            DisabledValue = 0,
            DefaultValue = 1
        },
        new()
        {
            Id = "advertising_contentdelivery",
            Title = "Реклама приложений в меню Пуск",
            Description = "Блокирует автоматическую установку промо-приложений (Candy Crush, Disney и т.д.) в меню Пуск.",
            Category = PrivacyCategory.Advertising,
            RegistryRoot = "HKCU",
            SubKeyPath = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            ValueName = "SubscribedContent-338388Enabled",
            DisabledValue = 0,
            DefaultValue = 1
        },
        new()
        {
            Id = "feedback_siuf",
            Title = "Запросы на обратную связь (SIUF)",
            Description = "Отключает всплывающие окна с просьбой оценить Windows или отправить отзыв.",
            Category = PrivacyCategory.FeedbackAndErrors,
            RegistryRoot = "HKCU",
            SubKeyPath = @"Software\Microsoft\Siuf\Rules",
            ValueName = "NumberOfSIUFInPeriod",
            DisabledValue = 0,
            DefaultValue = 1
        },
        new()
        {
            Id = "error_reporting_wer",
            Title = "Отчёты об ошибках Windows (WER)",
            Description = "Отключает фоновую отправку диагностических дампов сбоев на сервера Microsoft.",
            Category = PrivacyCategory.FeedbackAndErrors,
            RegistryRoot = "HKLM",
            SubKeyPath = @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting",
            ValueName = "Disabled",
            DisabledValue = 1,
            DefaultValue = 0
        },
        new()
        {
            Id = "customer_ceip",
            Title = "Программа улучшения качества (CEIP)",
            Description = "Отключает сбор данных об использовании функций и производительности программ.",
            Category = PrivacyCategory.FeedbackAndErrors,
            RegistryRoot = "HKLM",
            SubKeyPath = @"SOFTWARE\Policies\Microsoft\SQMClient\Windows",
            ValueName = "CEIPEnable",
            DisabledValue = 0,
            DefaultValue = 1
        },
        new()
        {
            Id = "sensors_location",
            Title = "Служба геолокации и датчиков",
            Description = "Блокирует фоновое определение физического местоположения устройства системой и службами.",
            Category = PrivacyCategory.TrackingAndSensors,
            RegistryRoot = "HKLM",
            SubKeyPath = @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors",
            ValueName = "DisableLocation",
            DisabledValue = 1,
            DefaultValue = 0
        },
        new()
        {
            Id = "tracking_activityfeed",
            Title = "Хронология действий (Activity Feed)",
            Description = "Отключает сохранение и синхронизацию истории открытых файлов и действий с облаком.",
            Category = PrivacyCategory.TrackingAndSensors,
            RegistryRoot = "HKLM",
            SubKeyPath = @"SOFTWARE\Policies\Microsoft\Windows\System",
            ValueName = "EnableActivityFeed",
            DisabledValue = 0,
            DefaultValue = 1
        }
    ];

    public IReadOnlyList<PrivacyTweakItem> GetTweakDefinitions() => _tweakDefinitions;

    public async Task<List<PrivacyTweakItem>> ScanStatusesAsync()
    {
        return await Task.Run(() =>
        {
            var result = new List<PrivacyTweakItem>();
            foreach (var item in _tweakDefinitions)
            {
                var clone = CloneTweak(item);
                clone.IsApplied = CheckIsTweakApplied(item);
                result.Add(clone);
            }
            return result;
        });
    }

    public async Task<bool> ApplyTweakAsync(string tweakId)
    {
        var tweak = _tweakDefinitions.FirstOrDefault(t => t.Id == tweakId);
        if (tweak == null) return false;

        return await Task.Run(async () =>
        {
            try
            {
                SaveBackupBeforeChange(tweak);

                if (!string.IsNullOrEmpty(tweak.ServiceName))
                {
                    var (serviceOk, serviceReason) = await ConfigureServiceAsync(tweak.ServiceName, disabled: true);
                    if (!serviceOk)
                    {
                        System.Diagnostics.Debug.WriteLine($"[PrivacyDebloat] Service disable failed for {tweakId}: {serviceReason}");
                        return false;
                    }
                }

                if (!string.IsNullOrEmpty(tweak.RegistryRoot) && !string.IsNullOrEmpty(tweak.SubKeyPath) && !string.IsNullOrEmpty(tweak.ValueName))
                {
                    SetRegistryValue(tweak.RegistryRoot, tweak.SubKeyPath, tweak.ValueName, tweak.DisabledValue ?? 0);
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PrivacyDebloat] Apply failed for {tweakId}: {ex.Message}");
                return false;
            }
        });
    }

    public async Task<bool> RevertTweakAsync(string tweakId)
    {
        var tweak = _tweakDefinitions.FirstOrDefault(t => t.Id == tweakId);
        if (tweak == null) return false;

        return await Task.Run(async () =>
        {
            try
            {
                if (!string.IsNullOrEmpty(tweak.ServiceName))
                {
                    var (serviceOk, serviceReason) = await ConfigureServiceAsync(tweak.ServiceName, disabled: false);
                    if (!serviceOk)
                    {
                        System.Diagnostics.Debug.WriteLine($"[PrivacyDebloat] Service restore failed for {tweakId}: {serviceReason}");
                        return false;
                    }
                }

                if (!string.IsNullOrEmpty(tweak.RegistryRoot) && !string.IsNullOrEmpty(tweak.SubKeyPath) && !string.IsNullOrEmpty(tweak.ValueName))
                {
                    RestoreRegistryValue(tweak);
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PrivacyDebloat] Revert failed for {tweakId}: {ex.Message}");
                return false;
            }
        });
    }

    public async Task<int> ApplyAllRecommendedAsync()
    {
        int appliedCount = 0;
        foreach (var tweak in _tweakDefinitions.Where(t => t.IsRecommended))
        {
            if (await ApplyTweakAsync(tweak.Id))
            {
                appliedCount++;
            }
        }
        return appliedCount;
    }

    public async Task<int> RestoreAllDefaultsAsync()
    {
        int restoredCount = 0;
        foreach (var tweak in _tweakDefinitions)
        {
            if (await RevertTweakAsync(tweak.Id))
            {
                restoredCount++;
            }
        }
        return restoredCount;
    }

    // Откат значения реестра: восстанавливает СОХРАНЁННОЕ в бэкапе состояние
    // (фактическое значение до первого изменения), а не DefaultValue из кода.
    // «Отсутствовало» в бэкапе = DeleteValue. Legacy-записи старой схемы
    // (константа вместо фактического значения) не доверяются: ставится
    // DefaultValue из кода с честным сообщением о fallback.
    private void RestoreRegistryValue(PrivacyTweakItem tweak)
    {
        var rootKeyName = tweak.RegistryRoot!;
        var subKeyPath = tweak.SubKeyPath!;
        var valueName = tweak.ValueName!;

        Dictionary<string, JsonElement>? backupMap;
        try
        {
            backupMap = LoadBackupMap();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PrivacyDebloat] Backup load failed for {tweak.Id}: {ex.Message}");
            backupMap = null;
        }

        PrivacyBackupEntry? entry = null;
        if (backupMap != null
            && backupMap.TryGetValue(tweak.Id, out var element)
            && element.ValueKind == JsonValueKind.Object)
        {
            entry = element.Deserialize<PrivacyBackupEntry>(BackupJsonOptions);
        }

        if (entry != null && TryRestoreFromBackupEntry(rootKeyName, subKeyPath, valueName, entry))
        {
            return; // исходное состояние восстановлено из бэкапа
        }

        // Бэкап не вернул исходное состояние: честно сообщаем причину
        // и ставим значение по умолчанию из кода
        string reason = entry != null
            ? "запись бэкапа повреждена или тип значения не поддерживается"
            : backupMap == null
                ? "файл бэкапа не читается"
                : backupMap.ContainsKey(tweak.Id)
                    ? "запись бэкапа старой схемы (константа вместо фактического значения до изменения)"
                    : "бэкапа для твика нет";
        LogBackupFallback(tweak, reason);

        SetRegistryValue(rootKeyName, subKeyPath, valueName, tweak.DefaultValue ?? 1);
    }

    private bool TryRestoreFromBackupEntry(string rootKeyName, string subKeyPath, string valueName, PrivacyBackupEntry entry)
    {
        if (!entry.Existed)
        {
            // Значения до изменения не было: восстановление «отсутствовало»
            _registry.DeleteValue(rootKeyName, subKeyPath, valueName);
            return true;
        }

        if (TryParseSavedRegistryValue(entry, out var value, out var kind))
        {
            _registry.SetValue(rootKeyName, subKeyPath, valueName, value, kind);
            return true;
        }

        return false;
    }

    private static bool TryParseSavedRegistryValue(PrivacyBackupEntry entry, out object value, out RegistryValueKind kind)
    {
        value = 0;
        kind = RegistryValueKind.Unknown;
        if (string.IsNullOrEmpty(entry.Kind) || string.IsNullOrEmpty(entry.Value))
        {
            return false;
        }

        if (!Enum.TryParse(entry.Kind, out kind))
        {
            return false;
        }

        try
        {
            switch (kind)
            {
                case RegistryValueKind.DWord:
                    value = int.Parse(entry.Value, CultureInfo.InvariantCulture);
                    return true;
                case RegistryValueKind.QWord:
                    value = long.Parse(entry.Value, CultureInfo.InvariantCulture);
                    return true;
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString:
                    value = entry.Value;
                    return true;
                default:
                    // MultiString/Binary и прочие типы не поддержаны:
                    // честный отказ, вызывающий делает fallback на DefaultValue
                    return false;
            }
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    // Честное сообщение о fallback-откате: исходное значение неизвестно,
    // восстановлено значение по умолчанию из кода
    private void LogBackupFallback(PrivacyTweakItem tweak, string reason)
    {
        var message = $"[PrivacyDebloat] Откат '{tweak.Id}': {reason} — исходное значение неизвестно, " +
                      $"восстановлено значение по умолчанию из кода ({tweak.DefaultValue ?? 1}).";
        Debug.WriteLine(message);
        _logger?.LogWarning("{Message}", message);
    }

    private static bool CheckIsTweakApplied(PrivacyTweakItem tweak)
    {
        try
        {
            if (!string.IsNullOrEmpty(tweak.ServiceName))
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{tweak.ServiceName}");
                if (key != null)
                {
                    var startVal = key.GetValue("Start");
                    if (startVal is int intVal)
                    {
                        return intVal == 4; // 4 = Disabled
                    }
                }
            }

            if (!string.IsNullOrEmpty(tweak.RegistryRoot) && !string.IsNullOrEmpty(tweak.SubKeyPath) && !string.IsNullOrEmpty(tweak.ValueName))
            {
                using var root = tweak.RegistryRoot == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
                using var key = root.OpenSubKey(tweak.SubKeyPath);
                if (key != null)
                {
                    var val = key.GetValue(tweak.ValueName);
                    if (val != null)
                    {
                        return val.ToString() == tweak.DisabledValue?.ToString();
                    }
                }
            }
        }
        catch
        {
            // Ignore access errors
        }

        return false;
    }

    // Запись значения с прежней семантикой типов: int → DWord, string → String,
    // прочие рантайм-типы — тип определяет реестр (RegistryValueKind.Unknown).
    private void SetRegistryValue(string rootKeyName, string subKeyPath, string valueName, object value)
    {
        _registry.SetValue(rootKeyName, subKeyPath, valueName, value, InferRegistryValueKind(value));
    }

    private static RegistryValueKind InferRegistryValueKind(object value) => value switch
    {
        int => RegistryValueKind.DWord,
        string => RegistryValueKind.String,
        _ => RegistryValueKind.Unknown
    };

    private static async Task<(bool Success, string Reason)> ConfigureServiceAsync(string serviceName, bool disabled)
    {
        string startArg = disabled ? "disabled" : "demand";
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
            return (false, $"sc config '{serviceName}' (код {config.ExitCode}): {config.Output}");
        }

        if (disabled)
        {
            var psiStop = new ProcessStartInfo
            {
                // Абсолютный путь из системного каталога — binary planting (M7),
                // см. psiConfig выше
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
                    return (false, $"net stop '{serviceName}' (код {stop.ExitCode}): {stop.Output}");
                }
            }
        }

        return (true, string.Empty);
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
            System.Diagnostics.Debug.WriteLine($"[PrivacyDebloat] Kill timed-out process failed: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"[PrivacyDebloat] sc query '{serviceName}' failed: {ex.Message}");
            return null;
        }
    }

    private void SaveBackupBeforeChange(PrivacyTweakItem tweak)
    {
        try
        {
            // Бэкапим только значения реестра: у сервисных твиков (ServiceName)
            // нет значения для бэкапа — их откат восстанавливает службу через sc.exe
            if (string.IsNullOrEmpty(tweak.RegistryRoot) || string.IsNullOrEmpty(tweak.SubKeyPath) || string.IsNullOrEmpty(tweak.ValueName))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_backupFilePath)!);
            var backupMap = LoadBackupMap();

            // Первый бэкап побеждает: храним исходное значение до ПЕРВОГО
            // изменения — повторные apply не затирают его применённым значением
            if (backupMap.ContainsKey(tweak.Id))
            {
                return;
            }

            // Фактическое состояние значения до изменения, а не константа из кода:
            // snapshot == null означает «значения не было» (откат = DeleteValue)
            var snapshot = _registry.GetValue(tweak.RegistryRoot, tweak.SubKeyPath, tweak.ValueName);
            backupMap[tweak.Id] = JsonSerializer.SerializeToElement(new PrivacyBackupEntry
            {
                Existed = snapshot != null,
                Kind = snapshot?.Kind.ToString(),
                Value = snapshot == null ? null : FormatRegistryValueForBackup(snapshot.Value)
            });
            File.WriteAllText(_backupFilePath, JsonSerializer.Serialize(backupMap, BackupJsonOptions));
        }
        catch (Exception ex)
        {
            // Backup should never block user action, but its absence must be visible
            System.Diagnostics.Debug.WriteLine($"[PrivacyDebloat] Backup save failed for {tweak.Id}: {ex.Message}");
        }
    }

    /// <summary>
    /// Загружает карту бэкапа: id твика → запись. Записи новой схемы (M4) —
    /// объекты { Existed, Kind, Value }, записи старой схемы — плоские строки
    /// (константы); при перезаписи файла сохраняются как есть.
    /// Бросает исключение на повреждённом файле — вызывающий решает, что делать.
    /// </summary>
    private Dictionary<string, JsonElement> LoadBackupMap()
    {
        if (!File.Exists(_backupFilePath))
        {
            return [];
        }

        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(_backupFilePath)) ?? [];
    }

    // Инвариантное строковое представление фактического значения реестра для бэкапа
    private static string? FormatRegistryValueForBackup(object value) => value switch
    {
        int intValue => intValue.ToString(CultureInfo.InvariantCulture),
        long longValue => longValue.ToString(CultureInfo.InvariantCulture),
        string stringValue => stringValue,
        _ => value.ToString()
    };

    // Запись бэкапа новой схемы (M4): фактическое состояние значения до изменения.
    // Записи старой схемы (до M4) — плоские строки-константы, откат им не доверяет.
    private sealed class PrivacyBackupEntry
    {
        public bool Existed { get; set; }
        public string? Kind { get; set; }
        public string? Value { get; set; }
    }

    private static PrivacyTweakItem CloneTweak(PrivacyTweakItem source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        Description = source.Description,
        Category = source.Category,
        IsApplied = source.IsApplied,
        IsRecommended = source.IsRecommended,
        RegistryRoot = source.RegistryRoot,
        SubKeyPath = source.SubKeyPath,
        ValueName = source.ValueName,
        DisabledValue = source.DisabledValue,
        DefaultValue = source.DefaultValue,
        ServiceName = source.ServiceName
    };
}
