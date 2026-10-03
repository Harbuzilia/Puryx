namespace SmartCleaner.Core.Safety;

/// <summary>
/// Результат <see cref="PathResolver.ResolveRealPath(string)"/>.
/// </summary>
/// <param name="Path">
/// Лучший известный путь. При <paramref name="Resolved"/> = <c>true</c> — реальный
/// путь без нерезолвнутых reparse-точек; при <c>false</c> — исходный нерезолвнутый
/// путь (fail-closed: гейт обязан отказаться от операции, а не использовать это значение).
/// </param>
/// <param name="Resolved">
/// <c>true</c> — путь развёрнут до реального (или reparse-точек на нём не было);
/// <c>false</c> — цепочку reparse-точек не удалось разрешить (цикл, лимит глубины,
/// неподдерживаемый тег, ошибка ФС).
/// </param>
internal readonly record struct PathResolveResult(string Path, bool Resolved);

/// <summary>
/// День 13 — M1 (findings M1, RED-этап): ЗАГЛУШКА, фиксирующая текущее поведение
/// проекта — путь нормализуется лексически (Path.GetFullPath), reparse-точки
/// (junction/symlink) НЕ резолвятся. Реальная реализация (посегментный обход,
/// защита от циклов) — следующим коммитом.
/// </summary>
internal static class PathResolver
{
    /// <summary>
    /// Разворачивает junction/symlink-цепочку до реального пути.
    /// ЗАГЛУШКА (RED-этап): лексическая нормализация без резолва reparse-точек.
    /// </summary>
    internal static PathResolveResult ResolveRealPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new PathResolveResult(string.Empty, Resolved: false);
        }

        try
        {
            return new PathResolveResult(Path.GetFullPath(path), Resolved: true);
        }
        catch
        {
            return new PathResolveResult(path, Resolved: false);
        }
    }
}
