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
/// Резолв реального пути через цепочки reparse-точек (junction/symlink).
/// День 13 — M1 (findings M1, ROADMAP): все проверки путей в проекте лексические
/// (Path.GetFullPath), reparse-точки не разворачиваются — whitelist-bypass
/// в user-scope (file-items, порождаемые через junction).
///
/// Алгоритм: посегментный обход предков текущего пути от корня; первый
/// (ближайший к корню) сегмент с атрибутом ReparsePoint разворачивается ровно
/// на один шаг (FileSystemInfo.ResolveLinkTarget), после чего обход
/// перезапускается для нового пути. Посегментная проверка обязательна:
/// файловые API сами разворачивают ПРОМЕЖУТОЧНЫЕ reparse-точки, поэтому
/// запрос атрибутов «всего пути» junction не выявляет — проверять нужно каждый
/// префикс, а разворачивать — начиная с ближайшего к корню.
///
/// Гарантии завершения:
/// 1) <see cref="MaxReparseHops"/> — жёсткий лимит числа переходов;
/// 2) множество посещённых reparse-точек (OrdinalIgnoreCase) — ранний детект
///    циклов (A→B→A, самоссылка); лимит остаётся страховкой для циклов,
///    невидимых множеству (например, один каталог под разными написаниями).
///
/// Fail-closed: при любой неоднозначности — ошибка доступа при проверке
/// сегмента, неподдерживаемый reparse-тег (OneDrive-плейсхолдеры и т.п.),
/// исключение при нормализации — резолв возвращает
/// <see cref="PathResolveResult.Resolved"/> = <c>false</c> и исходный путь;
/// гейт (День 14) обязан отказать в операции по такому пути.
///
/// Ограничение (зафиксировано сознательно): сегменты «..» в исходном пути
/// сворачиваются Path.GetFullPath лексически ДО резолва reparse-точек. Для
/// пути, содержащего «..» через junction, результат может отличаться от
/// фактической точки монтирования ФС; практические источники путей
/// (перечисление сканера, диалоги выбора) «..» не содержат. При
/// необходимости строгой семантики — обход со стеком сегментов без
/// предварительной свёртки.
/// </summary>
internal static class PathResolver
{
    /// <summary>
    /// Максимальное число разворотов reparse-точек на один резолв
    /// (страховка против слишком длинных и скрытых циклов).
    /// </summary>
    internal const int MaxReparseHops = 40;

    /// <summary>
    /// Разворачивает junction/symlink-цепочку до реального пути.
    /// Путь без reparse-точек возвращается нормализованным (Path.GetFullPath).
    /// </summary>
    internal static PathResolveResult ResolveRealPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new PathResolveResult(string.Empty, Resolved: false);
        }

        string current;
        try
        {
            current = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return Fail(path);
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int hop = 0; ; hop++)
        {
            var scan = FindNearestRootReparse(current, out var reparsePath, out var tail);
            if (scan == AncestorScanKind.NoReparse)
            {
                // reparse-точек на пути нет — путь реален.
                return new PathResolveResult(current, Resolved: true);
            }
            if (scan == AncestorScanKind.Ambiguous || hop >= MaxReparseHops)
            {
                // Неоднозначность или превышение лимита переходов — fail-closed.
                return Fail(path);
            }
            if (!visited.Add(reparsePath))
            {
                // Цикл: эта reparse-точка уже разворачивалась в текущем резолве.
                return Fail(path);
            }

            var targetPath = ResolveOneHop(reparsePath);
            if (targetPath is null)
            {
                return Fail(path);
            }

            try
            {
                // Цель symlink может быть относительной — привязка к каталогу ссылки.
                var rootedTarget = Path.IsPathRooted(targetPath)
                    ? targetPath
                    : Path.Combine(Path.GetDirectoryName(reparsePath) ?? string.Empty, targetPath);
                current = tail.Length == 0
                    ? Path.GetFullPath(rootedTarget)
                    : Path.GetFullPath(Path.Combine(rootedTarget, tail));
            }
            catch (Exception)
            {
                return Fail(path);
            }
        }
    }

    /// <summary>Fail-closed: исходный путь и признак неудачного резолва.</summary>
    private static PathResolveResult Fail(string originalPath) => new(originalPath, Resolved: false);

    private enum AncestorScanKind
    {
        /// <summary>Среди существующих сегментов reparse-точек нет.</summary>
        NoReparse,
        /// <summary>Сегмент проверить не удалось (не «не существует») — неоднозначно.</summary>
        Ambiguous,
        /// <summary>Найдена reparse-точка — первая от корня.</summary>
        Reparse
    }

    /// <summary>
    /// Ищет среди предков fullPath (включая сам путь) первую от корня reparse-точку.
    /// В tail — часть пути после найденной точки (для наращивания поверх цели).
    /// </summary>
    private static AncestorScanKind FindNearestRootReparse(string fullPath, out string reparsePath, out string tail)
    {
        reparsePath = string.Empty;
        tail = string.Empty;

        string root = Path.GetPathRoot(fullPath) ?? fullPath;
        if (fullPath.Length <= root.Length)
        {
            // Сам корень диска/UNC reparse-точкой быть не может.
            return AncestorScanKind.NoReparse;
        }

        string remainder = fullPath.Substring(root.Length);
        int pos = 0;
        while (pos < remainder.Length)
        {
            int nextSeparator = remainder.IndexOf(Path.DirectorySeparatorChar, pos);
            if (nextSeparator < 0)
            {
                nextSeparator = remainder.Length;
            }

            int segmentLength = nextSeparator - pos;
            if (segmentLength > 0)
            {
                string prefix = root + remainder.Substring(0, nextSeparator);
                switch (ProbeSegment(prefix))
                {
                    case SegmentProbe.Ambiguous:
                        return AncestorScanKind.Ambiguous;
                    case SegmentProbe.ReparsePoint:
                        reparsePath = prefix;
                        tail = nextSeparator + 1 < remainder.Length
                            ? remainder.Substring(nextSeparator + 1)
                            : string.Empty;
                        return AncestorScanKind.Reparse;
                }
            }

            pos = nextSeparator + 1;
        }

        return AncestorScanKind.NoReparse;
    }

    private enum SegmentProbe
    {
        /// <summary>Сегмент существует без reparse-атрибута либо не существует.</summary>
        NotReparse,
        /// <summary>Сегмент — reparse-точка (junction/symlink/...).</summary>
        ReparsePoint,
        /// <summary>Сегмент проверить не удалось — неоднозначность.</summary>
        Ambiguous
    }

    /// <summary>
    /// Проверяет сегмент пути как финальный компонент: GetAttributes НЕ следует
    /// по reparse-точке в финальном сегменте (возвращает атрибуты самой точки;
    /// работает и для «висячих» junction). Несуществующий сегмент — не ошибка:
    /// хвост резолвимого пути может не существовать; прочие ошибки —
    /// неоднозначность, fail-closed.
    /// </summary>
    private static SegmentProbe ProbeSegment(string prefix)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(prefix);
        }
        catch (FileNotFoundException)
        {
            return SegmentProbe.NotReparse;
        }
        catch (DirectoryNotFoundException)
        {
            return SegmentProbe.NotReparse;
        }
        catch (Exception)
        {
            return SegmentProbe.Ambiguous;
        }

        return (attributes & FileAttributes.ReparsePoint) != 0
            ? SegmentProbe.ReparsePoint
            : SegmentProbe.NotReparse;
    }

    /// <summary>
    /// Разворачивает reparse-точку на один шаг (FileSystemInfo.ResolveLinkTarget).
    /// null — точку развернуть не удалось: неподдерживаемый reparse-тег
    /// (OneDrive-плейсхолдеры и т.п.) или ошибка ФС; вызывающий отвечает fail-closed.
    /// </summary>
    private static string? ResolveOneHop(string reparsePath)
    {
        try
        {
            // Обёртка по атрибуту Directory: DirectoryInfo для junction и
            // каталоговых symlink, FileInfo для файловых symlink.
            var isDirectory = (File.GetAttributes(reparsePath) & FileAttributes.Directory) != 0;
            FileSystemInfo info = isDirectory ? new DirectoryInfo(reparsePath) : new FileInfo(reparsePath);
            var target = info.ResolveLinkTarget(returnFinalTarget: false);
            return string.IsNullOrWhiteSpace(target?.FullName) ? null : target.FullName;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
