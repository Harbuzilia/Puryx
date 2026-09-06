using SmartCleaner.App.ViewModels;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Models;
using System.Collections.Generic;

namespace SmartCleaner.App.Views;

/// <summary>
/// ViewModel для диалога предпросмотра очистки
/// </summary>
public class PreviewDialogViewModel
{
    public int ItemCount { get; set; }
    public string TotalSizeFormatted { get; set; } = "0 Б";
    public int ProtectedCount { get; set; }
    public int DeleteCount { get; set; }
    public int UninstallCount { get; set; }
    public string OperationCaption { get; set; } = "Будут выполнены операции:";
    public string WarningText { get; set; } = "Файлы будут перемещены в корзину. Вы сможете восстановить их при необходимости.";
    public List<ScannedItemViewModel> Items { get; set; } = new();

    public static PreviewDialogViewModel Create(IEnumerable<ScannedItemViewModel> items)
    {
        var list = items.ToList();
        var totalSize = list.Sum(i => i.Size);
        var deleteCount = list.Count(i => i.Item.ActionTarget == CleaningActionTarget.FileSystem);
        var uninstallCount = list.Count(i => i.Item.ActionTarget == CleaningActionTarget.Package);
        var hasMixed = deleteCount > 0 && uninstallCount > 0;
        var packageOnly = deleteCount == 0 && uninstallCount > 0;
        
        return new PreviewDialogViewModel
        {
            ItemCount = list.Count,
            TotalSizeFormatted = FormatSize(totalSize),
            ProtectedCount = 0, // Защищённые уже отфильтрованы
            DeleteCount = deleteCount,
            UninstallCount = uninstallCount,
            OperationCaption = hasMixed
                ? "Будут удалены файлы и выполнен uninstall пакетов:"
                : packageOnly
                    ? "Будет выполнен uninstall пакетов:"
                    : "Будут удалены:",
            WarningText = hasMixed
                ? "Часть элементов будет удалена в корзину, часть — деинсталлирована через пакетный менеджер."
                : packageOnly
                    ? "Вы запускаете uninstall пакетов в локальных проектах/venv. Операция необратима для текущего окружения."
                    : "Файлы будут перемещены в корзину. Вы сможете восстановить их при необходимости.",
            Items = list
        };
    }

    private static string FormatSize(long bytes) => SizeFormatter.Format(bytes);
}
