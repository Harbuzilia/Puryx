using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SmartCleaner.Core.Privacy;

public class PrivacyDebloatService
{
    private static readonly string BackupFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SmartCleaner",
        "privacy_backup.json");

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

        return await Task.Run(() =>
        {
            try
            {
                SaveBackupBeforeChange(tweak);

                if (!string.IsNullOrEmpty(tweak.ServiceName))
                {
                    ConfigureService(tweak.ServiceName, disabled: true);
                }

                if (!string.IsNullOrEmpty(tweak.RegistryRoot) && !string.IsNullOrEmpty(tweak.SubKeyPath) && !string.IsNullOrEmpty(tweak.ValueName))
                {
                    SetRegistryValue(tweak.RegistryRoot, tweak.SubKeyPath, tweak.ValueName, tweak.DisabledValue ?? 0);
                }

                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<bool> RevertTweakAsync(string tweakId)
    {
        var tweak = _tweakDefinitions.FirstOrDefault(t => t.Id == tweakId);
        if (tweak == null) return false;

        return await Task.Run(() =>
        {
            try
            {
                if (!string.IsNullOrEmpty(tweak.ServiceName))
                {
                    ConfigureService(tweak.ServiceName, disabled: false);
                }

                if (!string.IsNullOrEmpty(tweak.RegistryRoot) && !string.IsNullOrEmpty(tweak.SubKeyPath) && !string.IsNullOrEmpty(tweak.ValueName))
                {
                    SetRegistryValue(tweak.RegistryRoot, tweak.SubKeyPath, tweak.ValueName, tweak.DefaultValue ?? 1);
                }

                return true;
            }
            catch
            {
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

    private static void SetRegistryValue(string rootKeyName, string subKeyPath, string valueName, object value)
    {
        using var root = rootKeyName == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
        using var key = root.CreateSubKey(subKeyPath, writable: true);
        if (key != null)
        {
            if (value is int intVal)
            {
                key.SetValue(valueName, intVal, RegistryValueKind.DWord);
            }
            else if (value is string strVal)
            {
                key.SetValue(valueName, strVal, RegistryValueKind.String);
            }
            else
            {
                key.SetValue(valueName, value);
            }
        }
    }

    private static void ConfigureService(string serviceName, bool disabled)
    {
        try
        {
            string startArg = disabled ? "disabled" : "demand";
            var psiConfig = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"config {serviceName} start= {startArg}",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p1 = Process.Start(psiConfig);
            p1?.WaitForExit(3000);

            if (disabled)
            {
                var psiStop = new ProcessStartInfo
                {
                    FileName = "net.exe",
                    Arguments = $"stop {serviceName} /y",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p2 = Process.Start(psiStop);
                p2?.WaitForExit(3000);
            }
        }
        catch
        {
            // Ignore service start/stop exceptions
        }
    }

    private static void SaveBackupBeforeChange(PrivacyTweakItem tweak)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BackupFilePath)!);
            var backupMap = new Dictionary<string, string>();

            if (File.Exists(BackupFilePath))
            {
                var json = File.ReadAllText(BackupFilePath);
                backupMap = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
            }

            if (!backupMap.ContainsKey(tweak.Id))
            {
                backupMap[tweak.Id] = tweak.DefaultValue?.ToString() ?? "1";
                File.WriteAllText(BackupFilePath, JsonSerializer.Serialize(backupMap, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch
        {
            // Backup should never block user action
        }
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
