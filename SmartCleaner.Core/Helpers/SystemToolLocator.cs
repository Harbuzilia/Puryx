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
}
