namespace SmartCleaner.Core.Scanning;

/// <summary>
/// Общая конфигурация текущего сканирования — runtime параметры, которые
/// MainViewModel устанавливает перед запуском ScanAsync.
/// Зарегистрирован как Singleton в DI.
/// Thread-safe: запись из UI-потока, чтение из Task.Run.
/// </summary>
public class ScanConfiguration
{
    private volatile string? _customScanPath;

    /// <summary>
    /// Дополнительная папка, выбранная пользователем для сканирования.
    /// Null или пустая строка = не задана.
    /// </summary>
    public string? CustomScanPath
    {
        get => _customScanPath;
        set => _customScanPath = value;
    }
}
