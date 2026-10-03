using System.Diagnostics;
using System.Runtime.InteropServices;
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
                StandardOutputEncoding = GetSchtasksOutputEncoding()
            };

            using var process = Process.Start(psi);
            if (process == null) return;

            using var reader = process.StandardOutput;
            int count = 0;

            // Поток читаем до конца (не блокируем канал schtasks), но ограничиваем число
            // элементов: после рабочего фильтра \Microsoft\ на типичной машине остаются
            // единицы-десятки сторонних задач (на машине разработки — 38 из 402), поэтому
            // прежний лимит 50 забивался системными задачами и обрезал реальные записи.
            // 200 — запас против затопления списка (например, генераторы задач),
            // реальное множество задач автозагрузки не обрезает.
            while (reader.ReadLine() is { } line)
            {
                if (count >= MaxTaskSchedulerItems) continue; // дренаж остатка вывода

                var item = TryParseTaskSchedulerCsvLine(line);
                if (item == null) continue;

                item.Publisher = GetPublisher(item.FilePath);
                items.Add(item);
                count++;
            }

            process.WaitForExit(3000);
        }
        catch (Exception ex) { /* schtasks not available or permission denied */ Debug.WriteLine($"[StartupEngine] Task scheduler scan error: {ex.Message}"); }
    }

    /// <summary>Колонки вывода «schtasks /query /fo CSV /NH /V»: «Имя узла» (HostName).</summary>
    private const int SchtaskColumnHostName = 0;

    /// <summary>Колонки вывода /V: «Имя задачи» (путь вида \Папка\Задача) — имя берётся отсюда, а не из [0].</summary>
    private const int SchtaskColumnTaskName = 1;

    /// <summary>Колонки вывода /V: «Состояние».</summary>
    private const int SchtaskColumnStatus = 3;

    /// <summary>Колонки вывода /V: «Задача для выполнения» (команда).</summary>
    private const int SchtaskColumnTaskToRun = 8;

    /// <summary>
    /// Верхний предел элементов планировщика за одно сканирование (страховка от
    /// затопления списка задачами), см. ScanTaskScheduler.
    /// </summary>
    private const int MaxTaskSchedulerItems = 200;

    /// <summary>
    /// Разбирает строку вывода «schtasks /query /fo CSV /NH /V» в элемент автозагрузки;
    /// null — если строка отфильтрована (системная задача, COM-обработчик, N/A, неактивный статус).
    /// </summary>
    /// <remarks>
    /// Формат /V — 28 колонок (порядок сверен по строке заголовка живого вывода):
    /// [0] «Имя узла», [1] «Имя задачи», [3] «Состояние», [8] «Задача для выполнения».
    /// Обрамляющие кавычки полей снимает ParseCsvLine, поэтому Trim('"') здесь не нужен.
    /// </remarks>
    internal static StartupItem? TryParseTaskSchedulerCsvLine(string line)
    {
        var fields = ParseCsvLine(line);
        if (fields.Length <= SchtaskColumnTaskToRun) return null;

        var taskName = fields[SchtaskColumnTaskName];
        var status = fields[SchtaskColumnStatus];
        var taskToRun = fields[SchtaskColumnTaskToRun];

        // Фильтруем системные задачи Microsoft — по имени задачи (пути вида \Microsoft\…),
        // а не по колонке 0 «Имя узла» (имя машины, фильтр по нему никогда не срабатывал)
        if (taskName.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)) return null;
        if (taskToRun.Contains("COM handler", StringComparison.OrdinalIgnoreCase)) return null;
        if (string.IsNullOrWhiteSpace(taskToRun) || taskToRun == "N/A") return null;

        // Проверяем: логон-триггер = автозагрузка
        if (status.Contains("Ready", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Running", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Готово", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Выполняется", StringComparison.OrdinalIgnoreCase))
        {
            var (filePath, arguments) = ParseCommand(taskToRun);

            return new StartupItem
            {
                Name = Path.GetFileName(taskName),
                FilePath = filePath,
                Arguments = arguments,
                Command = taskToRun,
                Source = StartupSource.TaskScheduler,
                IsEnabled = true
            };
        }

        return null;
    }

    private static int _oemEncodingProviderRegistered;

    /// <summary>
    /// Кодировка вывода schtasks: OEM-страница консоли системы (GetOEMCP, а не хардкод 866 —
    /// на нерусских Windows консольные утилиты выводят в другой OEM-странице).
    /// </summary>
    internal static System.Text.Encoding GetSchtasksOutputEncoding()
    {
        EnsureOemEncodingProvider();
        return System.Text.Encoding.GetEncoding((int)GetOEMCP());
    }

    /// <summary>
    /// Регистрирует CodePagesEncodingProvider однократно (потокобезопасно):
    /// на .NET 8 без провайдера OEM-кодировки (866 и др.) недоступны — NotSupportedException.
    /// </summary>
    private static void EnsureOemEncodingProvider()
    {
        if (Interlocked.Exchange(ref _oemEncodingProviderRegistered, 1) != 0) return;

        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// OEM-кодовая страница консоли системы (P/Invoke kernel32).
    /// </summary>
    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    /// <summary>
    /// Декодирует байты вывода schtasks в строки (чистая функция — тестируется без запуска процесса).
    /// </summary>
    internal static IReadOnlyList<string> DecodeSchtasksOutput(byte[] rawBytes)
    {
        var encoding = GetSchtasksOutputEncoding();
        using var stream = new MemoryStream(rawBytes, writable: false);
        using var reader = new StreamReader(stream, encoding);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }
        return lines;
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
    /// Парсирует команду на путь к файлу и аргументы.
    /// </summary>
    internal static (string FilePath, string Arguments) ParseCommand(string command)
    {
        command = command.Trim();

        // Задвоенные кавычки вокруг пути («""C:\…exe"" …») встречаются в реальных
        // регистрациях задач (например, Realtek RtkAudUService64_BG) — путь между парами кавычек
        if (command.StartsWith("\"\"", StringComparison.Ordinal))
        {
            var endDoubleQuote = command.IndexOf("\"\"", 2, StringComparison.Ordinal);
            if (endDoubleQuote > 2)
            {
                var path = command[2..endDoubleQuote];
                var args = endDoubleQuote + 2 < command.Length ? command[(endDoubleQuote + 2)..].Trim() : "";
                return (path, args);
            }
        }

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
    /// Парсер CSV-строки вывода schtasks («/fo CSV»).
    /// </summary>
    /// <remarks>
    /// schtasks оборачивает каждое поле в кавычки, НЕ экранируя внутренние кавычки
    /// (не RFC 4180 — сверено с XML задач: «""C:\…exe" -arg» в выводе = обрамляющая
    /// кавычка поля + кавычка контента). Поэтому кавычка считается закрывающей
    /// обрамление только тогда, когда за ней следует запятая или конец строки;
    /// остальные кавычки (включая задвоенные «""») — часть контента и сохраняются.
    /// Обрамляющие кавычки снимаются, контент возвращается как есть; незакавыченные
    /// поля допускаются.
    /// </remarks>
    internal static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        int i = 0;

        while (i < line.Length)
        {
            current.Clear();

            if (line[i] == '"')
            {
                i++; // открывающая обрамляющая кавычка
                while (i < line.Length)
                {
                    // закрывающая обрамляющая: за кавычкой идёт разделитель полей или конец строки
                    if (line[i] == '"' && (i + 1 >= line.Length || line[i + 1] == ','))
                    {
                        i++; // снимаем закрывающую обрамляющую кавычку
                        break;
                    }
                    current.Append(line[i]);
                    i++;
                }
            }
            else
            {
                while (i < line.Length && line[i] != ',')
                {
                    current.Append(line[i]);
                    i++;
                }
            }

            fields.Add(current.ToString());

            if (i < line.Length && line[i] == ',') i++; // разделитель полей
        }

        return fields.ToArray();
    }
}
