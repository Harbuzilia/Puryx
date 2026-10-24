using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Localization;
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
            var sizeStr = sizeMatch.Success ? sizeMatch.Value : Localizer.GetString("Nlq_DefaultSize");

            response.Text = Localizer.GetString("Nlq_VideoText", sizeStr);
            response.ActionType = AiActionType.OpenPage;
            response.ActionPayload = "LargeFiles";
            response.ActionButtonText = Localizer.GetString("Nlq_VideoButton");
            return Task.FromResult(response);
        }

        // 2. Check for Uninstalling apps & Leftovers
        if (text.Contains("удали") || text.Contains("программ") || text.Contains("хвост") || text.Contains("uninstall") || text.Contains("деинстал"))
        {
            response.Text = Localizer.GetString("Nlq_UninstallText");
            response.ActionType = AiActionType.OpenPage;
            response.ActionPayload = "Uninstaller";
            response.ActionButtonText = Localizer.GetString("Nlq_UninstallButton");
            return Task.FromResult(response);
        }

        // 3. Check for Dev Caches
        if (text.Contains("node") || text.Contains("npm") || text.Contains("rust") || text.Contains("разработк") || text.Contains("wsl") || text.Contains("dev") || text.Contains("gradle") || text.Contains("maven"))
        {
            response.Text = Localizer.GetString("Nlq_DevText");
            response.ActionType = AiActionType.OpenPage;
            response.ActionPayload = "DevClean";
            response.ActionButtonText = Localizer.GetString("Nlq_DevButton");
            return Task.FromResult(response);
        }

        // 4. Check for Deep WinSxS / Drivers
        if (text.Contains("драйвер") || text.Contains("winsxs") || text.Contains("ядро") || text.Contains("driverstore"))
        {
            response.Text = Localizer.GetString("Nlq_DeepText");
            response.ActionType = AiActionType.OpenPage;
            response.ActionPayload = "DeepClean";
            response.ActionButtonText = Localizer.GetString("Nlq_DeepButton");
            return Task.FromResult(response);
        }

        // 5. Check for Duplicate Files
        if (text.Contains("дубликат") || text.Contains("копи") || text.Contains("повтор") || text.Contains("duplicate"))
        {
            response.Text = Localizer.GetString("Nlq_DuplicatesText");
            response.ActionType = AiActionType.OpenPage;
            response.ActionPayload = "Duplicates";
            response.ActionButtonText = Localizer.GetString("Nlq_DuplicatesButton");
            return Task.FromResult(response);
        }

        // 6. Check for Compression / CompactOS
        if (text.Contains("сжат") || text.Contains("сожм") || text.Contains("сжатие") || text.Contains("compact") || text.Contains("lzx") || text.Contains("компресс"))
        {
            response.Text = Localizer.GetString("Nlq_CompactText");
            response.ActionType = AiActionType.OpenPage;
            response.ActionPayload = "Compact";
            response.ActionButtonText = Localizer.GetString("Nlq_CompactButton");
            return Task.FromResult(response);
        }

        // 7. Check for Generic Space Freeing request ("освободи 20 гб", "нужно место", "очисти")
        if (text.Contains("освободи") || text.Contains("место") || text.Contains("очист") || text.Contains("free up") || text.Contains("clean"))
        {
            response.Text = Localizer.GetString("Nlq_CleanText");
            response.ActionType = AiActionType.TriggerScan;
            response.ActionPayload = "All";
            response.ActionButtonText = Localizer.GetString("Nlq_CleanButton");
            return Task.FromResult(response);
        }

        // 6. Generic Intelligent Helper Advice
        response.Text = Localizer.GetString("Nlq_DefaultText", userQuery);
        response.ActionType = AiActionType.TriggerScan;
        response.ActionPayload = "General";
        response.ActionButtonText = Localizer.GetString("Nlq_DefaultButton");

        return Task.FromResult(response);
    }
}
