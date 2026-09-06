namespace SmartCleaner.Core.Models;

/// <summary>
/// Сводка по категории сканирования для отображения в sidebar
/// </summary>
public record CategorySummary
{
    /// <summary>Отображаемое имя категории</summary>
    public required string Name { get; init; }

    /// <summary>Иконка из Segoe MDL2 Assets (Unicode строка)</summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>Суммарный размер элементов в категории (байты)</summary>
    public long TotalSize { get; init; }

    /// <summary>Количество элементов в категории</summary>
    public int ItemCount { get; init; }

    /// <summary>Является ли эта запись специальным элементом "Все категории"</summary>
    public bool IsAll { get; init; }

    /// <summary>Топ-3 самых крупных элемента для tooltip</summary>
    public IReadOnlyList<string> TopItems { get; init; } = [];

    /// <summary>Текст tooltip для sidebar</summary>
    public string TooltipText => TopItems.Count > 0
        ? string.Join("\n", TopItems)
        : $"{ItemCount} элементов";
}
