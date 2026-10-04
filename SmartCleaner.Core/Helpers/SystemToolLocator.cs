using System.IO;

namespace SmartCleaner.Core.Helpers;

// Резолвит системные утилиты по абсолютному пути из настоящего системного каталога.
// Запуск по неквалифицированному имени (FileName = "compact.exe") ищет exe сначала
// в каталоге приложения, а в portable-распространении он доступен пользователю
// на запись — binary planting (аудит M7).
internal static class SystemToolLocator
{
    // 32-битный процесс на 64-битной ОС видит System32 как SysWOW64;
    // настоящий системный каталог для него доступен через виртуальный alias SysNative.
    private static readonly string NativeSystemDirectory =
        !Environment.Is64BitProcess && Environment.Is64BitOperatingSystem
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysNative")
            : Environment.SystemDirectory;

    internal static string GetCompactPath() => Path.Combine(NativeSystemDirectory, "compact.exe");

    internal static string GetDismPath() => Path.Combine(NativeSystemDirectory, "dism.exe");

    internal static string GetPnputilPath() => Path.Combine(NativeSystemDirectory, "pnputil.exe");

    /// <summary>sc.exe — конфигурация и запрос состояния системных служб.</summary>
    internal static string GetScPath() => Path.Combine(NativeSystemDirectory, "sc.exe");

    /// <summary>net.exe — запуск/остановка системных служб (net start/stop).</summary>
    internal static string GetNetPath() => Path.Combine(NativeSystemDirectory, "net.exe");

    /// <summary>ipconfig.exe — диагностика сетевых интерфейсов и DNS-кэша.</summary>
    internal static string GetIpconfigPath() => Path.Combine(NativeSystemDirectory, "ipconfig.exe");

    /// <summary>netsh.exe — конфигурация сетевого стека.</summary>
    internal static string GetNetshPath() => Path.Combine(NativeSystemDirectory, "netsh.exe");

    /// <summary>schtasks.exe — управление задачами планировщика Windows.</summary>
    internal static string GetSchtasksPath() => Path.Combine(NativeSystemDirectory, "schtasks.exe");

    /// <summary>powercfg.exe — схемы питания и управление питанием.</summary>
    internal static string GetPowercfgPath() => Path.Combine(NativeSystemDirectory, "powercfg.exe");

    /// <summary>
    /// Системный Windows PowerShell (System32\WindowsPowerShell\v1.0). Все текущие
    /// сайты Core вызывают Windows-специфичные системные командлеты
    /// (Get-NetAdapter, Set-DnsClientServerAddress, Get-PhysicalDisk,
    /// Get-StorageReliabilityCounter) — их семантика «системный Windows PowerShell»,
    /// поэтому путь обязан быть абсолютным. Если будущему сайту нужна иная семантика
    /// («любой pwsh из PATH»), он обязан НЕ использовать этот метод и явно
    /// задокументировать выбор; внешние dev-инструменты (docker, npm) ищутся в PATH
    /// осознанно — в системном каталоге их нет, binary planting для них неприменим.
    /// </summary>
    internal static string GetWindowsPowerShellPath() =>
        Path.Combine(NativeSystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>
    /// explorer.exe — Проводник (День 24, reviewer P3). Живёт в корне каталога
    /// Windows: в System32 explorer.exe отсутствует (живой замер), поэтому
    /// резолв из Windows-корня, а не из NativeSystemDirectory. Абсолютный путь
    /// обязателен: запуск по неквалифицированному имени ищет exe в каталоге
    /// приложения — binary planting (M7).
    /// </summary>
    internal static string GetExplorerPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
}
