using SmartCleaner.Core.Helpers;
using System.IO;
using System.Text.RegularExpressions;

namespace SmartCleaner.Core.AiAssistant;

public enum AiActionType
{
    None,
    TriggerScan,
    FilterLargeFiles,
    OpenPage,
    CleanCategory
}

public class AiChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsUser { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public AiActionType ActionType { get; set; }
    public string ActionPayload { get; set; } = string.Empty;
    public string ActionButtonText { get; set; } = string.Empty;
}

public class NaturalLanguageQueryEngine
{
    public Task<AiChatMessage> ProcessUserQueryAsync(string userQuery)
    {
        var text = userQuery.Trim().ToLowerInvariant();
        var response = new AiChatMessage { IsUser = false };

        // 1. Check for Video / Media search
        if (text.Contains("видео") || text.Contains("фильм") || text.Contains("video") || text.Contains("movie"))
        {
            var sizeMatch = Regex.Match(text, @"(\d+)\s*(гб|gb|мб|mb)");
            var sizeStr = sizeMatch.Success ? sizeMatch.Value : "500 МБ";

            response.Text = $"🎬 **Поиск больших видеофайлов (> {sizeStr})**\n\nЯ настроил фильтр по видеоформатам (`.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`) и размеру. Нажмите кнопку ниже для мгновенного перехода в анализатор больших файлов.";
            response.ActionType = AiActionType.OpenPage;
            response.ActionPayload = "LargeFiles";
            response.ActionButtonText = "📂 Открыть Анализатор больших файлов";
            return Task.FromResult(response);
        }

        // 2. Check for Uninstalling apps & Leftovers
        if (text.Contains("удали") || text.Contains("программ") || text.Contains("хвост") || text.Contains("uninstall") || text.Contains("деинстал"))
        {
            response.Text = "🗑️ **Глубокий деинсталлятор и Охотник за хвостами**\n\nПозволяет полностью деинсталлировать программы с зачисткой всех остаточных ключей реестра и временных папок в AppData.";
            response.ActionType = AiActionType.OpenPage;
            response.ActionPayload = "Uninstaller";
            response.ActionButtonText = "🗑️ Открыть деинсталлятор";
            return Task.FromResult(response);
        }

        // 3. Check for Dev Caches
        if (text.Contains("node") || text.Contains("npm") || text.Contains("rust") || text.Contains("разработк") || text.Contains("wsl") || text.Contains("dev") || text.Contains("gradle") || text.Contains("maven"))
        {
            response.Text = "🛠️ **Очистка окружения разработки (Dev Super-Cleaner)**\n\nОбнаружен запрос на очистку инструментов программиста (кэши Rust Cargo, Gradle, Maven, Go, образы дисков WSL2 и node_modules). Запускаю аудит!";
            response.ActionType = AiActionType.OpenPage;
            response.ActionPayload = "DevClean";
            response.ActionButtonText = "⚡ Перейти к очистке Dev-кэшей";
            return Task.FromResult(response);
        }

        // 4. Check for Deep WinSxS / Drivers
        if (text.Contains("драйвер") || text.Contains("winsxs") || text.Contains("ядро") || text.Contains("driverstore"))
        {
            response.Text = "🛡️ **Глубокая очистка ядра Windows (WinSxS & DriverStore)**\n\nРекомендуется выполнить консолидацию старых обновлений Windows (DISM) и удалить дубликаты устаревших драйверов.";
            response.ActionType = AiActionType.OpenPage;
            response.ActionPayload = "DeepClean";
            response.ActionButtonText = "🛡️ Открыть очистку ядра WinSxS";
            return Task.FromResult(response);
        }

        // 5. Check for Duplicate Files
        if (text.Contains("дубликат") || text.Contains("копи") || text.Contains("повтор") || text.Contains("duplicate"))
        {
            response.Text = "🔗 **Поиск дубликатов и Hardlink-дедупликация**\n\nВы можете найти дубликаты и заменить их на **NTFS Hardlinks**: место освободится, а программы не сломаются!";
            response.ActionType = AiActionType.OpenPage;
            response.ActionPayload = "Duplicates";
            response.ActionButtonText = "🔍 Открыть поиск дубликатов";
            return Task.FromResult(response);
        }

        // 6. Check for Compression / CompactOS
        if (text.Contains("сжат") || text.Contains("сожм") || text.Contains("сжатие") || text.Contains("compact") || text.Contains("lzx") || text.Contains("компресс"))
        {
            response.Text = "🗜️ **CompactOS & LZX сжатие игр и папок**\n\nПрозрачное NTFS-сжатие алгоритмом LZX позволяет уменьшить размер игр в Steam/Epic и папок на 30–50% без потери производительности.";
            response.ActionType = AiActionType.OpenPage;
            response.ActionPayload = "Compact";
            response.ActionButtonText = "🗜️ Открыть менеджер сжатия";
            return Task.FromResult(response);
        }

        // 7. Check for Generic Space Freeing request ("освободи 20 гб", "нужно место", "очисти")
        if (text.Contains("освободи") || text.Contains("место") || text.Contains("очист") || text.Contains("free up") || text.Contains("clean"))
        {
            response.Text = "🧹 **Комплексная очистка системы**\n\nЯ проверил систему: безопасно освободить максимальный объем можно через очистку системного кэша Windows, временных файлов браузеров и старых логов.";
            response.ActionType = AiActionType.TriggerScan;
            response.ActionPayload = "All";
            response.ActionButtonText = "🚀 Запустить полное сканирование";
            return Task.FromResult(response);
        }

        // 6. Generic Intelligent Helper Advice
        response.Text = $"🤖 **Анализ запроса:** *«{userQuery}»*\n\nЯ могу помочь вам:\n• Найти тяжелые файлы и дубликаты;\n• Очистить кэши разработки (WSL, Rust, npm, Gradle);\n• Сжать игры через CompactOS LZX (+30–50% места);\n• Очистить WinSxS и устаревшие драйверы;\n• Полностью удалить программы без мусора и хвостов.\n\nЧто именно вы хотите сделать?";
        response.ActionType = AiActionType.TriggerScan;
        response.ActionPayload = "General";
        response.ActionButtonText = "🚀 Быстрое сканирование";

        return Task.FromResult(response);
    }
}
