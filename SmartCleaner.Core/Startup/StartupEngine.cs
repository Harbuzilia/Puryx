using System.Diagnostics;
using Microsoft.Win32;

namespace SmartCleaner.Core.Startup;

/// <summary>
/// Движок анализа автозагрузки Windows.
/// Сканирует реестр (HKCU/HKLM Run), папки автозагрузки, планировщик заданий.
/// Позволяет включать/отключать/удалять записи.
/// </summary>
public sealed class StartupEngine
{
    // Ключи реестра для автозагрузки
    private const string RunKeyHKCU = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunKeyHKLM = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunKeyWow = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Сканирует все источники автозагрузки и возвращает список элементов.
    /// </summary>
    public List<StartupItem> ScanAll()
    {
        var items = new List<StartupItem>();

        // 1. Реестр: HKCU\...\Run
        ScanRegistryKey(Registry.CurrentUser, RunKeyHKCU, StartupSource.RegistryCurrentUser, items);

        // 2. Реестр: HKLM\...\Run
        ScanRegistryKey(Registry.LocalMachine, RunKeyHKLM, StartupSource.RegistryLocalMachine, items);

        // 3. Реестр: HKLM\...\Run (WOW6432)
        ScanRegistryKey(Registry.LocalMachine, RunKeyWow, StartupSource.RegistryWow6432, items);

        // 4. Папка автозагрузки текущего пользователя
        ScanStartupFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            StartupSource.StartupFolder, items);

        // 5. Общая папка автозагрузки
        ScanStartupFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            StartupSource.CommonStartupFolder, items);

        // 6. Планировщик заданий (основные)
        ScanTaskScheduler(items);

        return items;
    }

    /// <summary>
    /// Отключает элемент автозагрузки (для реестра — переименовывает с "!").
    /// </summary>
    /// <param name="item">Элемент для отключения.</param>
    /// <returns>true если успешно.</returns>
    public bool DisableItem(StartupItem item)
    {
        try
        {
            switch (item.Source)
            {
                case StartupSource.RegistryCurrentUser:
                case StartupSource.RegistryLocalMachine:
                case StartupSource.RegistryWow6432:
                    return DisableRegistryItem(item);

                case StartupSource.StartupFolder:
                case StartupSource.CommonStartupFolder:
                    // Переименовываем файл (добавляем .disabled)
                    if (File.Exists(item.FilePath))
                    {
                        File.Move(item.FilePath, item.FilePath + ".disabled");
                        item.IsEnabled = false;
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[StartupEngine] DisableItem error: {ex.Message}"); return false; }
    }

    /// <summary>
    /// Включает ранее отключённый элемент.
    /// </summary>
    public bool EnableItem(StartupItem item)
    {
        try
        {
            switch (item.Source)
            {
                case StartupSource.RegistryCurrentUser:
                case StartupSource.RegistryLocalMachine:
                case StartupSource.RegistryWow6432:
                    return EnableRegistryItem(item);

                case StartupSource.StartupFolder:
                case StartupSource.CommonStartupFolder:
                    var disabledPath = item.FilePath + ".disabled";
                    if (File.Exists(disabledPath))
                    {
                        File.Move(disabledPath, item.FilePath);
                        item.IsEnabled = true;
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[StartupEngine] EnableItem error: {ex.Message}"); return false; }
    }

    /// <summary>
    /// Удаляет элемент автозагрузки.
    /// </summary>
    public bool DeleteItem(StartupItem item)
    {
        try
        {
            switch (item.Source)
            {
                case StartupSource.RegistryCurrentUser:
                    using (var key = Registry.CurrentUser.OpenSubKey(item.RegistryPath, writable: true))
                    {
                        key?.DeleteValue(item.RegistryValueName, throwOnMissingValue: false);
                    }
                    return true;

                case StartupSource.RegistryLocalMachine:
                case StartupSource.RegistryWow6432:
                    using (var key = Registry.LocalMachine.OpenSubKey(item.RegistryPath, writable: true))
                    {
                        key?.DeleteValue(item.RegistryValueName, throwOnMissingValue: false);
                    }
                    return true;

                case StartupSource.StartupFolder:
                case StartupSource.CommonStartupFolder:
                    if (File.Exists(item.FilePath))
                        File.Delete(item.FilePath);
                    else if (File.Exists(item.FilePath + ".disabled"))
                        File.Delete(item.FilePath + ".disabled");
                    return true;

                default:
                    return false;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[StartupEngine] DeleteItem error: {ex.Message}"); return false; }
    }

    // ─── Реестр ────────────────────────────────

    /// <summary>
    /// Сканирует ветку реестра Run.
    /// </summary>
    private static void ScanRegistryKey(RegistryKey root, string subKey, StartupSource source, List<StartupItem> items)
    {
        try
        {
            using var key = root.OpenSubKey(subKey, writable: false);
            if (key == null) return;

            foreach (var valueName in key.GetValueNames())
            {
                var command = key.GetValue(valueName)?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(command)) continue;

                var (filePath, arguments) = ParseCommand(command);
                var publisher = GetPublisher(filePath);

                items.Add(new StartupItem
                {
                    Name = valueName,
                    FilePath = filePath,
                    Arguments = arguments,
                    Command = command,
                    Source = source,
                    IsEnabled = true,
                    Publisher = publisher,
                    RegistryPath = subKey,
                    RegistryValueName = valueName
                });
            }
        }
        catch (Exception ex) { /* insufficient permissions */ Debug.WriteLine($"[StartupEngine] Registry scan error: {ex.Message}"); }
    }

    /// <summary>
    /// Сканирует папку автозагрузки.
    /// </summary>
    private static void ScanStartupFolder(string folderPath, StartupSource source, List<StartupItem> items)
    {
        if (!Directory.Exists(folderPath)) return;

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(folderPath))
            {
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                bool isDisabled = ext == ".disabled";
                var actualPath = isDisabled ? filePath[..^".disabled".Length] : filePath;

                string targetPath;
                string arguments = "";

                if (ext == ".lnk" || (isDisabled && Path.GetExtension(actualPath).ToLowerInvariant() == ".lnk"))
                {
                    // Ярлык — получаем целевой путь через COM Shell
                    (targetPath, arguments) = ResolveShortcut(filePath);
                }
                else
                {
                    targetPath = actualPath;
                }

                items.Add(new StartupItem
                {
                    Name = Path.GetFileNameWithoutExtension(actualPath),
                    FilePath = isDisabled ? actualPath : filePath,
                    Arguments = arguments,
                    Command = targetPath + (string.IsNullOrEmpty(arguments) ? "" : " " + arguments),
                    Source = source,
                    IsEnabled = !isDisabled,
                    Publisher = GetPublisher(targetPath)
                });
            }
        }
        catch (Exception ex) { /* insufficient permissions */ Debug.WriteLine($"[StartupEngine] Startup folder scan error: {ex.Message}"); }
    }

    /// <summary>
    /// Сканирует планировщик заданий (через schtasks).
    /// </summary>
    private static void ScanTaskScheduler(List<StartupItem> items)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/query /fo CSV /NH /V",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.GetEncoding(866)
            };

            using var process = Process.Start(psi);
            if (process == null) return;

            using var reader = process.StandardOutput;
            int count = 0;

            while (!reader.EndOfStream && count < 50) // ограничиваем
            {
                var line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var fields = ParseCsvLine(line);
                if (fields.Length < 9) continue;

                var taskName = fields[0].Trim('"');
                var status = fields[3].Trim('"');
                var taskToRun = fields[8].Trim('"');

                // Фильтруем системные задачи Microsoft
                if (taskName.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)) continue;
                if (taskToRun.Contains("COM handler", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrWhiteSpace(taskToRun) || taskToRun == "N/A") continue;

                // Проверяем: логон-триггер = автозагрузка
                if (status.Contains("Ready", StringComparison.OrdinalIgnoreCase)
                    || status.Contains("Running", StringComparison.OrdinalIgnoreCase)
                    || status.Contains("Готово", StringComparison.OrdinalIgnoreCase)
                    || status.Contains("Выполняется", StringComparison.OrdinalIgnoreCase))
                {
                    var (filePath, arguments) = ParseCommand(taskToRun);

                    items.Add(new StartupItem
                    {
                        Name = Path.GetFileName(taskName),
                        FilePath = filePath,
                        Arguments = arguments,
                        Command = taskToRun,
                        Source = StartupSource.TaskScheduler,
                        IsEnabled = true,
                        Publisher = GetPublisher(filePath)
                    });
                    count++;
                }
            }

            process.WaitForExit(3000);
        }
        catch (Exception ex) { /* schtasks not available or permission denied */ Debug.WriteLine($"[StartupEngine] Task scheduler scan error: {ex.Message}"); }
    }

    // ─── Реестровые операции ────────────────────

    /// <summary>
    /// Отключает запись в реестре (удаляет из Run, сохраняет в Run\Disabled).
    /// </summary>
    private static bool DisableRegistryItem(StartupItem item)
    {
        var root = item.Source == StartupSource.RegistryCurrentUser
            ? Registry.CurrentUser
            : Registry.LocalMachine;

        using var runKey = root.OpenSubKey(item.RegistryPath, writable: true);
        if (runKey == null) return false;

        var value = runKey.GetValue(item.RegistryValueName);
        if (value == null) return false;

        // Сохраняем в подключ Disabled
        var disabledPath = item.RegistryPath + @"\AutorunsDisabled";
        using var disabledKey = root.CreateSubKey(disabledPath);
        disabledKey?.SetValue(item.RegistryValueName, value);

        runKey.DeleteValue(item.RegistryValueName, throwOnMissingValue: false);
        item.IsEnabled = false;
        return true;
    }

    /// <summary>
    /// Включает ранее отключённую запись реестра.
    /// </summary>
    private static bool EnableRegistryItem(StartupItem item)
    {
        var root = item.Source == StartupSource.RegistryCurrentUser
            ? Registry.CurrentUser
            : Registry.LocalMachine;

        var disabledPath = item.RegistryPath + @"\AutorunsDisabled";
        using var disabledKey = root.OpenSubKey(disabledPath, writable: true);
        if (disabledKey == null) return false;

        var value = disabledKey.GetValue(item.RegistryValueName);
        if (value == null) return false;

        using var runKey = root.OpenSubKey(item.RegistryPath, writable: true);
        runKey?.SetValue(item.RegistryValueName, value);

        disabledKey.DeleteValue(item.RegistryValueName, throwOnMissingValue: false);
        item.IsEnabled = true;
        return true;
    }

    // ─── Helpers ────────────────────────────────

    /// <summary>
    /// Парсит команду на путь к файлу и аргументы.
    /// </summary>
    private static (string FilePath, string Arguments) ParseCommand(string command)
    {
        command = command.Trim();

        if (command.StartsWith('"'))
        {
            var endQuote = command.IndexOf('"', 1);
            if (endQuote > 0)
            {
                var path = command[1..endQuote];
                var args = endQuote + 1 < command.Length ? command[(endQuote + 1)..].Trim() : "";
                return (path, args);
            }
        }

        var spaceIdx = command.IndexOf(' ');
        if (spaceIdx > 0)
        {
            return (command[..spaceIdx], command[(spaceIdx + 1)..].Trim());
        }

        return (command, "");
    }

    /// <summary>
    /// Получает издателя из свойств файла.
    /// </summary>
    private static string GetPublisher(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return "";
            var vi = FileVersionInfo.GetVersionInfo(filePath);
            return vi.CompanyName ?? "";
        }
        catch (Exception ex) { Debug.WriteLine($"[StartupEngine] GetPublisher error: {ex.Message}"); return ""; }
    }

    /// <summary>
    /// Разрешает ярлык .lnk через Windows Script Host COM.
    /// </summary>
    private static (string TargetPath, string Arguments) ResolveShortcut(string lnkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return (lnkPath, "");

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return (lnkPath, "");

            dynamic shortcut = shell.CreateShortcut(lnkPath);
            string targetPath = shortcut.TargetPath ?? "";
            string arguments = shortcut.Arguments ?? "";
            return (targetPath, arguments);
        }
        catch
        {
            return (lnkPath, "");
        }
    }

    /// <summary>
    /// Простой парсер CSV-строки.
    /// </summary>
    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        foreach (char c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                current.Append(c);
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
